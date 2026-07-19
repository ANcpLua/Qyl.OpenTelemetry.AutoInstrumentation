using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

var client = new ProbeClient(
    new BasicHttpBinding
    {
        OpenTimeout = TimeSpan.FromMilliseconds(200),
        SendTimeout = TimeSpan.FromMilliseconds(200),
    },
    new EndpointAddress($"http://{IPAddress.Loopback}:9/probe"));

try
{
    _ = client.Echo("sync");
}
catch (CommunicationException exception)
{
    Console.WriteLine("expected-wcf-error=" + exception.GetType().Name);
}

try
{
    _ = await client.EchoAsync("async");
}
catch (CommunicationException exception)
{
    Console.WriteLine("expected-wcf-error=" + exception.GetType().Name);
}

var report = WcfClientReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealWcfClientJsonContext.Default.WcfClientReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

[ServiceContract]
public interface IProbeService
{
    [OperationContract]
    string Echo(string value);

    [OperationContract]
    Task<string> EchoAsync(string value);
}

public sealed class ProbeClient : ClientBase<IProbeService>, IProbeService
{
    public ProbeClient(Binding binding, EndpointAddress remoteAddress)
        : base(binding, remoteAddress)
    {
    }

    public string Echo(string value) => Channel.Echo(value);

    public Task<string> EchoAsync(string value) => Channel.EchoAsync(value);
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record WcfClientReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static WcfClientReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var wcfSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "rpc.wcf.client"))
            .ToArray();

        if (wcfSpans.Length != 2)
            failures.Add($"expected 2 WCF client spans, got {wcfSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        RequireMethod(wcfSpans, nameof(ProbeClient.Echo), failures);
        RequireMethod(wcfSpans, nameof(ProbeClient.EchoAsync), failures);

        foreach (var span in wcfSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "WCF CLIENT"))
                failures.Add($"unexpected WCF span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected WCF span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected WCF span status Error, got {span.Status}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes.SystemValues.DotnetWcf, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(CommunicationException).FullName!, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes.Full, failures);
        }

        return new WcfClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), wcfSpans);
    }

    private static void RequireMethod(IEnumerable<CapturedActivity> activities, string expectedMethod, ICollection<string> failures)
    {
        if (!activities.Any(activity =>
                activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes.Method, out var method) &&
                StringComparer.Ordinal.Equals(method, expectedMethod)))
        {
            failures.Add($"missing WCF method span {expectedMethod}");
        }
    }

    private static void RequireTag(CapturedActivity activity, string key, string expected, ICollection<string> failures)
    {
        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequireMissingTag(CapturedActivity activity, string key, ICollection<string> failures)
    {
        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(WcfClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealWcfClientJsonContext : JsonSerializerContext;
