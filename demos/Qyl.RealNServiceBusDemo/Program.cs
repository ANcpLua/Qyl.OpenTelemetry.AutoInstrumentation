using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using OpenTelemetry.Trace;
using Qyl;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using NservicebusAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Nservicebus.NservicebusAttributes;
using OtelAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Otel.OtelAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

// The real registration path: AddQyl subscribes to NServiceBus's own ActivitySource and installs
// the one native-span processor.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealNServiceBusDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-nservicebus-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

var storageDirectory = Path.Combine(Path.GetTempPath(), "qyl-nservicebus-learning");
if (Directory.Exists(storageDirectory))
    Directory.Delete(storageDirectory, recursive: true);

var configuration = new EndpointConfiguration("qyl-probe");
var routing = configuration.UseTransport(new LearningTransport { StorageDirectory = storageDirectory });
routing.RouteToEndpoint(typeof(ProbeCommand), "qyl-probe");
var serialization = configuration.UseSerialization<SystemJsonSerializer>();
serialization.Options(new JsonSerializerOptions
{
    TypeInfoResolver = ProbeMessageJsonContext.Default,
});

builder.Logging.ClearProviders();
builder.Services.AddNServiceBusEndpoint(configuration);

using (var host = builder.Build())
{
    await host.StartAsync();
    var session = host.Services.GetRequiredService<IMessageSession>();

    await session.Publish(new ProbeEvent("alpha"));
    Console.WriteLine("published=alpha");

    await session.Send(new ProbeCommand("beta"));
    await ProbeCommandHandler.Handled.Task.WaitAsync(TimeSpan.FromSeconds(30));
    Console.WriteLine("sent-and-handled=beta");

    try
    {
        await session.Send(new UnroutedCommand("gamma"));
    }
    catch (Exception exception) when (exception.GetType() == typeof(Exception))
    {
        Console.WriteLine("expected-nservicebus-error=no-route");
    }

    host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
    await host.StopAsync();
}

Directory.Delete(storageDirectory, recursive: true);

var report = NServiceBusReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealNServiceBusJsonContext.Default.NServiceBusReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

/// <summary>Probe event published through the real endpoint.</summary>
public sealed class ProbeEvent(string name) : IEvent
{
    /// <summary>Probe payload name.</summary>
    public string Name { get; init; } = name;
}

/// <summary>Probe command routed back to this endpoint.</summary>
public sealed class ProbeCommand(string name) : ICommand
{
    /// <summary>Probe payload name.</summary>
    public string Name { get; init; } = name;
}

/// <summary>Command without a configured route, proving the send error path.</summary>
public sealed class UnroutedCommand(string name) : ICommand
{
    /// <summary>Probe payload name.</summary>
    public string Name { get; init; } = name;
}

/// <summary>Handler proving the routed command really round-trips through the transport.</summary>
public sealed class ProbeCommandHandler : IHandleMessages<ProbeCommand>
{
    /// <summary>Signals that the command was received by the endpoint.</summary>
    public static TaskCompletionSource Handled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task Handle(ProbeCommand message, IMessageHandlerContext context)
    {
        Handled.TrySetResult();
        return Task.CompletedTask;
    }
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record NServiceBusReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    // NServiceBus's own span names, ActivityNames in NServiceBus.Core. The outgoing pipeline names
    // the span after the intent, the incoming pipeline names it "process message", and the handler
    // invocation is named after the handler type.
    private const string SendSpanName = "send message";
    private const string PublishSpanName = "publish event";
    private const string ProcessSpanName = "process message";

    // NServiceBus's own values for nservicebus.message_intent.
    private const string SendIntent = "Send";
    private const string PublishIntent = "Publish";

    public static NServiceBusReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();

        var spans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.NServiceBusCore))
            .ToArray();

        // One outgoing span per operation, not one per run: the publish, the routed send and the
        // send that cannot be routed. The incoming pipeline adds the consumer span the interceptor
        // never produced at all.
        RequireExactlyOne(
            spans.Where(static span => IsIntent(span, PublishIntent) && StringComparer.Ordinal.Equals(span.Name, PublishSpanName)),
            "publish event span",
            failures);
        RequireExactlyOne(
            spans.Where(static span =>
                IsIntent(span, SendIntent) &&
                StringComparer.Ordinal.Equals(span.Name, SendSpanName)),
            "routed send message span",
            failures);
        RequireExactlyOne(
            spans.Where(static span =>
                StringComparer.Ordinal.Equals(span.Name, SendSpanName) &&
                span.Tags.ContainsKey(OtelAttributes.StatusCode)),
            "failed send message span",
            failures);
        RequireExactlyOne(
            spans.Where(static span => StringComparer.Ordinal.Equals(span.Name, ProcessSpanName)),
            "process message span",
            failures);

        foreach (var span in spans)
        {
            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.MessagingNServiceBus,
                failures);

            // The output change, asserted rather than described: NServiceBus publishes no messaging
            // semantic conventions at all, and qyl does not invent them on its behalf.
            RequireAbsentTag(span, MessagingAttributes.System, failures);
            RequireAbsentTag(span, MessagingAttributes.OperationName, failures);
            RequireAbsentTag(span, MessagingAttributes.OperationType, failures);
        }

        // NServiceBus's own vocabulary on the spans that carry a message.
        foreach (var span in spans.Where(static span => !StringComparer.Ordinal.Equals(span.Name, ProcessSpanName)
            && span.Tags.ContainsKey(NservicebusAttributes.MessageId)))
        {
            RequirePresentTag(span, NservicebusAttributes.MessageIntent, failures);
            RequirePresentTag(span, NservicebusAttributes.ConversationId, failures);
            RequirePresentTag(span, NservicebusAttributes.EnclosedMessageTypes, failures);
            RequirePresentTag(span, NservicebusAttributes.Version, failures);
        }

        var processSpan = spans.FirstOrDefault(static span =>
            StringComparer.Ordinal.Equals(span.Name, ProcessSpanName));
        if (processSpan is not null)
        {
            RequirePresentTag(processSpan, NservicebusAttributes.NativeMessageId, failures);
            if (!StringComparer.Ordinal.Equals(processSpan.Kind, "Consumer"))
                failures.Add($"expected the process span kind Consumer, got {processSpan.Kind}");
        }

        return new NServiceBusReport(runtimeMode, failures.Count is 0, failures.ToArray(), spans);
    }

    private static bool IsIntent(CapturedActivity span, string intent)
        => span.Tags.TryGetValue(NservicebusAttributes.MessageIntent, out var actual) &&
           StringComparer.Ordinal.Equals(actual, intent);

    private static void RequireExactlyOne(IEnumerable<CapturedActivity> matches, string label, ICollection<string> failures)
    {
        var count = matches.Count();
        if (count != 1)
            failures.Add($"expected exactly 1 {label}, got {count.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on {span.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key} on {span.Name}");
    }

    private static void RequireAbsentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (span.Tags.ContainsKey(key))
            failures.Add($"unexpected {key} on {span.Name}");
    }
}

[JsonSerializable(typeof(NServiceBusReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealNServiceBusJsonContext : JsonSerializerContext;

/// <summary>Source-generated JSON metadata so message serialization works under NativeAOT.</summary>
[JsonSerializable(typeof(ProbeEvent))]
[JsonSerializable(typeof(ProbeCommand))]
[JsonSerializable(typeof(UnroutedCommand))]
public sealed partial class ProbeMessageJsonContext : JsonSerializerContext;
