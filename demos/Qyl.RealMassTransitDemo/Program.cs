using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Qyl.AutoInstrumentation;
using Qyl.RealMassTransitDemo;

var uriText = Environment.GetEnvironmentVariable("QYL_RABBITMQ_URI");
if (string.IsNullOrWhiteSpace(uriText))
{
    Console.Error.WriteLine("QYL_RABBITMQ_URI is required.");
    return 2;
}

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var services = new ServiceCollection();
services.AddMassTransit(configure => configure.UsingRabbitMq((_, rabbit) =>
{
    rabbit.Host(new Uri(uriText));
    rabbit.ConfigureJsonSerializerOptions(options =>
    {
        options.TypeInfoResolverChain.Add(ProbeMessageJsonContext.Default);
        return options;
    });
}));

await using (var provider = services.BuildServiceProvider())
{
    var bus = provider.GetRequiredService<IBusControl>();
    await WaitForBusAsync(bus);

    await bus.Publish(new ProbeEvent("alpha"));
    Console.WriteLine("published=alpha");

    var sendEndpoint = await bus.GetSendEndpoint(new Uri("queue:qyl-probe"));
    await sendEndpoint.Send(new ProbeCommand("beta"));
    Console.WriteLine("sent=beta");

    try
    {
        await bus.Publish(new BrokenEvent("gamma"));
    }
    catch (ArgumentException exception)
    {
        Console.WriteLine("expected-masstransit-error=" + exception.GetType().Name);
    }

    await bus.StopAsync();
}

var report = MassTransitReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealMassTransitJsonContext.Default.MassTransitReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForBusAsync(IBusControl bus)
{
    Exception? lastException = null;

    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            await bus.StartAsync(TimeSpan.FromSeconds(5));
            return;
        }
        catch (Exception exception) when (exception is RabbitMqConnectionException or OperationCanceledException or TimeoutException)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("RabbitMQ did not become ready for MassTransit.", lastException);
}

/// <summary>Namespace-less message type that MassTransit deterministically rejects inside the intercepted publish.</summary>
public sealed record BrokenEvent(string Name);

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

internal sealed record MassTransitReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static MassTransitReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var massTransitSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.MessagingMassTransit))
            .ToArray();

        if (massTransitSpans.Length != 3)
            failures.Add($"expected 3 MassTransit message spans, got {massTransitSpans.Length}");

        var publishSuccess = FindByOperationAndStatus(massTransitSpans, QylSemanticAttributes.MessagingOperationNamePublish, "Unset");
        var sendSuccess = FindByOperationAndStatus(massTransitSpans, QylSemanticAttributes.MessagingOperationNameSend, "Unset");
        var publishError = FindByOperationAndStatus(massTransitSpans, QylSemanticAttributes.MessagingOperationNamePublish, "Error");

        Require(publishSuccess, "successful publish span", failures);
        Require(sendSuccess, "successful send span", failures);
        Require(publishError, "error publish span", failures);
        RequireTag(publishError, QylSemanticAttributes.ErrorType, "ArgumentException", failures);

        foreach (var span in massTransitSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "MassTransit message"))
                failures.Add($"unexpected MassTransit span name: {span.Name}");

            RequireTag(span, QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemMassTransit, failures);
            RequireTag(span, QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend, failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Producer"))
                failures.Add($"expected kind Producer, got {span.Kind}");
        }

        return new MassTransitReport(runtimeMode, failures.Count is 0, failures.ToArray(), massTransitSpans);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(QylSemanticAttributes.MessagingOperationName, out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation));

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
}

[JsonSerializable(typeof(MassTransitReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMassTransitJsonContext : JsonSerializerContext;
