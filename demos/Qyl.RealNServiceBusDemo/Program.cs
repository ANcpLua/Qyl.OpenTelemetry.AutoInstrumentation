using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
var capturedMetrics = new List<CapturedMetric>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

using var meterListener = new MeterListener();
meterListener.InstrumentPublished = static (instrument, listener) =>
{
    if (StringComparer.Ordinal.Equals(instrument.Meter.Name, QylMetricMeters.NServiceBusMeterName) &&
        StringComparer.Ordinal.Equals(instrument.Name, QylMetricNames.NServiceBusMessagingOperationDuration))
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meterListener.SetMeasurementEventCallback<double>(
    (instrument, measurement, tags, _) => capturedMetrics.Add(CapturedMetric.From(instrument, measurement, tags)));
meterListener.Start();

var storageDirectory = Path.Combine(Path.GetTempPath(), "qyl-nservicebus-learning");
if (Directory.Exists(storageDirectory))
    Directory.Delete(storageDirectory, recursive: true);

var configuration = new EndpointConfiguration("qyl-probe");
var routing = configuration.UseTransport(new LearningTransport { StorageDirectory = storageDirectory });
routing.RouteToEndpoint(typeof(ProbeCommand), "qyl-probe");
var serialization = configuration.UseSerialization<SystemJsonSerializer>();
serialization.Options(new System.Text.Json.JsonSerializerOptions
{
    TypeInfoResolver = ProbeMessageJsonContext.Default,
});

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Logging.ClearProviders();
hostBuilder.Services.AddNServiceBusEndpoint(configuration);

using (var host = hostBuilder.Build())
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

    await host.StopAsync();
}

Directory.Delete(storageDirectory, recursive: true);

var report = NServiceBusReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray(),
    capturedMetrics.ToArray());

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

internal sealed record CapturedMetric(
    string MeterName,
    string Name,
    double Value,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedMetric From(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        => new(
            instrument.Meter.Name,
            instrument.Name,
            value,
            TagsToDictionary(tags));

    private static Dictionary<string, string> TagsToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var values = new Dictionary<string, string>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
            values[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;

        return values;
    }
}

internal sealed record NServiceBusReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities,
    CapturedMetric[] Metrics)
{
    public static NServiceBusReport Create(
        string runtimeMode,
        CapturedActivity[] activities,
        CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        var nServiceBusSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.MessagingNServiceBus))
            .ToArray();
        var nServiceBusMetrics = metrics
            .Where(static metric =>
                StringComparer.Ordinal.Equals(metric.MeterName, QylMetricMeters.NServiceBusMeterName) &&
                StringComparer.Ordinal.Equals(metric.Name, QylMetricNames.NServiceBusMessagingOperationDuration))
            .ToArray();

        if (nServiceBusSpans.Length != 3)
            failures.Add($"expected 3 NServiceBus message spans, got {nServiceBusSpans.Length}");
        if (nServiceBusMetrics.Length != 3)
            failures.Add($"expected 3 NServiceBus duration measurements, got {nServiceBusMetrics.Length}");

        var publishSuccess = FindByOperationAndStatus(nServiceBusSpans, QylSemanticAttributes.MessagingOperationNamePublish, "Unset");
        var sendSuccess = FindByOperationAndStatus(nServiceBusSpans, QylSemanticAttributes.MessagingOperationNameSend, "Unset");
        var sendError = FindByOperationAndStatus(nServiceBusSpans, QylSemanticAttributes.MessagingOperationNameSend, "Error");
        var publishMetrics = FindMetricsByOperation(nServiceBusMetrics, QylSemanticAttributes.MessagingOperationNamePublish);
        var sendMetrics = FindMetricsByOperation(nServiceBusMetrics, QylSemanticAttributes.MessagingOperationNameSend);

        Require(publishSuccess, "successful publish span", failures);
        Require(sendSuccess, "successful send span", failures);
        Require(sendError, "error send span", failures);
        RequireTag(sendError, QylSemanticAttributes.ErrorType, "Exception", failures);
        if (publishMetrics.Length != 1)
            failures.Add($"expected 1 publish duration measurement, got {publishMetrics.Length}");
        if (sendMetrics.Length != 2)
            failures.Add($"expected 2 send duration measurements, got {sendMetrics.Length}");

        foreach (var span in nServiceBusSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "NServiceBus message"))
                failures.Add($"unexpected NServiceBus span name: {span.Name}");

            RequireTag(span, QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemNServiceBus, failures);
            RequireTag(span, QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend, failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Producer"))
                failures.Add($"expected kind Producer, got {span.Kind}");
        }

        foreach (var metric in nServiceBusMetrics)
        {
            if (metric.Value < 0)
                failures.Add($"expected non-negative NServiceBus duration, got {metric.Value.ToString(CultureInfo.InvariantCulture)}");

            RequireMetricTag(metric, QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemNServiceBus, failures);
            RequireMetricTag(metric, QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend, failures);
        }

        return new NServiceBusReport(runtimeMode, failures.Count is 0, failures.ToArray(), nServiceBusSpans, nServiceBusMetrics);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(QylSemanticAttributes.MessagingOperationName, out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation));

    private static CapturedMetric[] FindMetricsByOperation(IEnumerable<CapturedMetric> metrics, string operation)
        => metrics.Where(metric =>
            metric.Tags.TryGetValue(QylSemanticAttributes.MessagingOperationName, out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation)).ToArray();

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequireMetricTag(CapturedMetric metric, string key, string expected, ICollection<string> failures)
    {
        if (!metric.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing metric {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected metric {key}={expected}, got {actual}");
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
