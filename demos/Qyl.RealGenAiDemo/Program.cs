using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Qyl;
using static ProbeContract;

var activities = new ConcurrentQueue<CapturedActivity>();
var measurements = new ConcurrentQueue<CapturedMeasurement>();
var publishedMeterNames = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var exportedActivities = new List<Activity>();
var exportedMetrics = new List<Metric>();

var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealGenAiDemo",
    DisableDefaults = true,
});
builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-genai-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities))
    .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

using var host = builder.Build();
await host.StartAsync();

using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name is ExtensionsSourceName or AgentsSourceName or WorkflowsSourceName,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => activities.Enqueue(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

using var meterListener = new MeterListener
{
    InstrumentPublished = (instrument, listener) =>
    {
        var meterName = instrument.Meter.Name;
        if (meterName is ExtensionsSourceName or AgentsSourceName ||
            meterName.Contains("Agents.AI.Workflows", StringComparison.Ordinal))
        {
            publishedMeterNames.TryAdd(meterName, 0);
            listener.EnableMeasurementEvents(instrument);
        }
    },
};
meterListener.SetMeasurementEventCallback<int>(CaptureMeasurement);
meterListener.SetMeasurementEventCallback<long>(CaptureMeasurement);
meterListener.SetMeasurementEventCallback<double>(CaptureMeasurement);
meterListener.Start();

var bareResponseVerified = await RunBareMeAiAsync();
var agentResponseVerified = await RunAgentAsync();
var workflowOutputVerified = await RunWorkflowAsync();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
host.Services.GetRequiredService<MeterProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = GenAiReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    bareResponseVerified,
    agentResponseVerified,
    workflowOutputVerified,
    activities.ToArray(),
    measurements.ToArray(),
    publishedMeterNames.Keys.Order(StringComparer.Ordinal).ToArray(),
    exportedActivities
        .Select(static activity => activity.Source.Name)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray(),
    exportedMetrics
        .Select(static metric => metric.MeterName)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray());

Console.WriteLine(JsonSerializer.Serialize(report, RealGenAiJsonContext.Default.GenAiReport));
return report.Pass ? 0 : 1;

void CaptureMeasurement<T>(
    Instrument instrument,
    T measurement,
    ReadOnlySpan<KeyValuePair<string, object?>> tags,
    object? state)
    where T : struct
{
    _ = state;
    measurements.Enqueue(CapturedMeasurement.From(instrument, measurement, tags));
}

static async Task<bool> RunBareMeAiAsync()
{
    using IChatClient client = new FixedChatClient()
        .AsBuilder()
        .UseOpenTelemetry()
        .Build();

    var response = await client.GetResponseAsync(
        [new ChatMessage(ChatRole.User, UserSensitiveContent)]);

    var streamedText = new List<string>();
    await foreach (var update in client.GetStreamingResponseAsync(
                       [new ChatMessage(ChatRole.User, UserSensitiveContent)]))
    {
        if (!string.IsNullOrEmpty(update.Text))
            streamedText.Add(update.Text);
    }

    return StringComparer.Ordinal.Equals(response.Text, AssistantSensitiveContent) &&
           StringComparer.Ordinal.Equals(string.Concat(streamedText), AssistantSensitiveContent);
}

static async Task<bool> RunAgentAsync()
{
    using var innerClient = new FixedChatClient();
    var innerAgent = new ChatClientAgent(
        innerClient,
        new ChatClientAgentOptions
        {
            Id = "fixed-agent-id",
            Name = "fixed-agent",
            Description = "fixed agent telemetry probe",
            ChatOptions = new ChatOptions
            {
                Instructions = "Return the fixed response.",
            },
        });

    using var agent = (OpenTelemetryAgent)innerAgent
        .AsBuilder()
        .UseOpenTelemetry()
        .Build();

    var response = await agent.RunAsync(UserSensitiveContent);
    return StringComparer.Ordinal.Equals(response.Text, AssistantSensitiveContent);
}

static async Task<bool> RunWorkflowAsync()
{
    Func<string, string> uppercase = static value => value.ToUpperInvariant();
    Func<string, string> suffix = static value => value + "|complete";
    var uppercaseExecutor = uppercase.BindAsExecutor("uppercase");
    var suffixExecutor = suffix.BindAsExecutor("suffix");

    var workflow = new WorkflowBuilder(uppercaseExecutor)
        .AddEdge(uppercaseExecutor, suffixExecutor)
        .WithOutputFrom(suffixExecutor)
        .WithOpenTelemetry()
        .Build();

    await using var run = await InProcessExecution.RunAsync(workflow, WorkflowSensitiveContent);
    return run.NewEvents
        .OfType<WorkflowOutputEvent>()
        .Any(static output => StringComparer.Ordinal.Equals(
            Convert.ToString(output.Data, CultureInfo.InvariantCulture),
            "PRIVATE-WORKFLOW-CONTENT|complete"));
}

internal sealed class FixedChatClient : IChatClient
{
    private static readonly ChatClientMetadata Metadata = new(
        FixedProvider,
        new Uri("https://example.invalid"),
        FixedModel);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _ = options;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _ = options;
        foreach (var update in CreateResponse().ToChatResponseUpdates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;

        if (serviceType == typeof(ChatClientMetadata))
            return Metadata;

        return serviceType == typeof(IChatClient) || serviceType == typeof(FixedChatClient)
            ? this
            : null;
    }

    public void Dispose()
    {
    }

    private static ChatResponse CreateResponse()
        => new(new ChatMessage(ChatRole.Assistant, AssistantSensitiveContent)
        {
            MessageId = "fixed-message-id",
        })
        {
            ResponseId = "fixed-response-id",
            ModelId = FixedModel,
            CreatedAt = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero),
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails
            {
                InputTokenCount = 7,
                OutputTokenCount = 3,
                TotalTokenCount = 10,
            },
        };
}

internal sealed record CapturedActivity(
    string SourceName,
    string Name,
    string Kind,
    string Status,
    string SpanId,
    string ParentSpanId,
    Dictionary<string, string> Tags,
    string[] EventTagValues)
{
    public static CapturedActivity From(Activity activity)
    {
        var tags = activity.TagObjects.ToDictionary(
            static tag => tag.Key,
            static tag => TelemetryValue.Format(tag.Value),
            StringComparer.Ordinal);
        var eventTagValues = activity.Events
            .SelectMany(static activityEvent => activityEvent.Tags)
            .Select(static tag => TelemetryValue.Format(tag.Value))
            .ToArray();

        return new CapturedActivity(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.SpanId.ToHexString(),
            activity.ParentSpanId.ToHexString(),
            tags,
            eventTagValues);
    }
}

internal sealed record CapturedMeasurement(
    string MeterName,
    string InstrumentName,
    string Value,
    Dictionary<string, string> Tags)
{
    public static CapturedMeasurement From<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var capturedTags = new Dictionary<string, string>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
            capturedTags.Add(tag.Key, TelemetryValue.Format(tag.Value));

        return new CapturedMeasurement(
            instrument.Meter.Name,
            instrument.Name,
            Convert.ToString(measurement, CultureInfo.InvariantCulture) ?? string.Empty,
            capturedTags);
    }
}

internal sealed record GenAiReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    bool BareResponseVerified,
    bool AgentResponseVerified,
    bool WorkflowOutputVerified,
    CapturedActivity[] Activities,
    CapturedMeasurement[] Measurements,
    string[] PublishedMeterNames,
    string[] QylExportedActivitySources,
    string[] QylExportedMeterNames)
{
    private const string OperationDuration = "gen_ai.client.operation.duration";
    private const string TokenUsage = "gen_ai.client.token.usage";
    private const string TimeToFirstChunk = "gen_ai.client.operation.time_to_first_chunk";
    private const string TimePerOutputChunk = "gen_ai.client.operation.time_per_output_chunk";

    public static GenAiReport Create(
        string runtimeMode,
        bool bareResponseVerified,
        bool agentResponseVerified,
        bool workflowOutputVerified,
        CapturedActivity[] activities,
        CapturedMeasurement[] measurements,
        string[] publishedMeterNames,
        string[] qylExportedActivitySources,
        string[] qylExportedMeterNames)
    {
        var failures = new List<string>();
        Require(bareResponseVerified, "bare MEAI response did not traverse the fixed client", failures);
        Require(agentResponseVerified, "agent response did not traverse the fixed client", failures);
        Require(workflowOutputVerified, "workflow did not produce the expected deterministic output", failures);

        ValidateMeAiActivities(activities, failures);
        ValidateAgentActivities(activities, failures);
        ValidateWorkflowActivities(activities, failures);
        ValidateSensitiveData(activities, measurements, failures);
        ValidateMetrics(measurements, publishedMeterNames, failures);
        ValidateQylRegistration(qylExportedActivitySources, qylExportedMeterNames, failures);

        return new GenAiReport(
            runtimeMode,
            failures.Count is 0,
            failures.ToArray(),
            bareResponseVerified,
            agentResponseVerified,
            workflowOutputVerified,
            activities,
            measurements,
            publishedMeterNames,
            qylExportedActivitySources,
            qylExportedMeterNames);
    }

    private static void ValidateQylRegistration(
        string[] activitySources,
        string[] meterNames,
        ICollection<string> failures)
    {
        var expectedSources = new List<string>(3);
        AddExpected(
            expectedSources,
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED",
            ExtensionsSourceName);
        AddExpected(
            expectedSources,
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED",
            AgentsSourceName);
        AddExpected(
            expectedSources,
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAIWORKFLOWS_INSTRUMENTATION_ENABLED",
            WorkflowsSourceName);
        Require(
            activitySources.Order(StringComparer.Ordinal).SequenceEqual(expectedSources.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            $"Qyl.Sdk activity registration mismatch: expected={string.Join('|', expectedSources)} actual={string.Join('|', activitySources)}",
            failures);

        RequireRegistration(
            meterNames,
            "OTEL_DOTNET_AUTO_METRICS_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED",
            ExtensionsSourceName,
            failures);
        RequireRegistration(
            meterNames,
            "OTEL_DOTNET_AUTO_METRICS_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED",
            AgentsSourceName,
            failures);
        Require(
            meterNames.All(static name => !name.Contains("Agents.AI.Workflows", StringComparison.Ordinal)),
            "Qyl.Sdk exported an unexpected Workflow meter",
            failures);
    }

    private static void AddExpected(List<string> names, string variable, string telemetryName)
    {
        if (IsEnabled(variable))
            names.Add(telemetryName);
    }

    private static void RequireRegistration(
        IEnumerable<string> names,
        string variable,
        string telemetryName,
        ICollection<string> failures)
    {
        var registered = names.Contains(telemetryName, StringComparer.Ordinal);
        Require(
            registered == IsEnabled(variable),
            $"Qyl.Sdk meter registration mismatch for {telemetryName}: registered={registered}",
            failures);
    }

    private static bool IsEnabled(string variable)
        => !string.Equals(Environment.GetEnvironmentVariable(variable), "false", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMeAiActivities(
        IEnumerable<CapturedActivity> activities,
        ICollection<string> failures)
    {
        var spans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(activity.SourceName, ExtensionsSourceName))
            .ToArray();
        if (spans.Length != 2)
            failures.Add($"expected 2 bare MEAI spans, got {spans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in spans)
            ValidateChatSpan(span, failures);

        Require(
            spans.Any(static span => span.Tags.TryGetValue("gen_ai.request.stream", out var value) && value == "True"),
            "missing streaming MEAI span",
            failures);
    }

    private static void ValidateAgentActivities(
        IEnumerable<CapturedActivity> activities,
        ICollection<string> failures)
    {
        var spans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(activity.SourceName, AgentsSourceName))
            .ToArray();
        var invokeSpans = spans
            .Where(static span => HasTag(span, "gen_ai.operation.name", "invoke_agent"))
            .ToArray();
        var chatSpans = spans
            .Where(static span => HasTag(span, "gen_ai.operation.name", "chat"))
            .ToArray();

        Require(invokeSpans.Length == 1, $"expected 1 invoke_agent span, got {invokeSpans.Length.ToString(CultureInfo.InvariantCulture)}", failures);
        Require(chatSpans.Length == 1, $"expected 1 agent chat span, got {chatSpans.Length.ToString(CultureInfo.InvariantCulture)}", failures);

        foreach (var span in invokeSpans)
        {
            ValidateChatSpan(span, failures, "invoke_agent");
            RequireTag(span, "gen_ai.agent.id", "fixed-agent-id", failures);
            RequireTag(span, "gen_ai.agent.name", "fixed-agent", failures);
            RequireTag(span, "gen_ai.agent.description", "fixed agent telemetry probe", failures);
        }

        foreach (var span in chatSpans)
            ValidateChatSpan(span, failures);

        if (invokeSpans.Length == 1 && chatSpans.Length == 1)
        {
            Require(
                StringComparer.Ordinal.Equals(chatSpans[0].ParentSpanId, invokeSpans[0].SpanId),
                "agent chat span did not form a nested telemetry path",
                failures);
        }
    }

    private static void ValidateWorkflowActivities(
        IEnumerable<CapturedActivity> activities,
        ICollection<string> failures)
    {
        var spans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(activity.SourceName, WorkflowsSourceName))
            .ToArray();
        RequireActivity(spans, "workflow.build", failures);
        RequireActivity(spans, "workflow.session", failures);
        RequireActivity(spans, "workflow_invoke", failures);
        Require(
            spans.Count(static span => span.Name.StartsWith("executor.process ", StringComparison.Ordinal)) >= 2,
            "expected at least 2 workflow executor.process spans",
            failures);
        RequireActivity(spans, "message.send", failures);
    }

    private static void ValidateSensitiveData(
        IEnumerable<CapturedActivity> activities,
        IEnumerable<CapturedMeasurement> measurements,
        ICollection<string> failures)
    {
        string[] forbiddenKeys =
        [
            "gen_ai.input.messages",
            "gen_ai.output.messages",
            "gen_ai.system_instructions",
            "executor.input",
            "executor.output",
            "message.content",
        ];
        string[] forbiddenValues =
        [
            UserSensitiveContent,
            AssistantSensitiveContent,
            WorkflowSensitiveContent,
            WorkflowSensitiveContent.ToUpperInvariant(),
        ];

        foreach (var activity in activities)
        {
            foreach (var key in forbiddenKeys)
            {
                if (activity.Tags.ContainsKey(key))
                    failures.Add($"sensitive telemetry tag was present: {activity.SourceName}:{activity.Name}:{key}");
            }

            foreach (var value in activity.Tags.Values.Append(activity.Name).Concat(activity.EventTagValues))
            {
                foreach (var forbiddenValue in forbiddenValues)
                {
                    if (value.Contains(forbiddenValue, StringComparison.Ordinal))
                        failures.Add($"sensitive content leaked through {activity.SourceName}:{activity.Name}");
                }
            }
        }

        foreach (var measurement in measurements)
        {
            foreach (var value in measurement.Tags.Values)
            {
                foreach (var forbiddenValue in forbiddenValues)
                {
                    if (value.Contains(forbiddenValue, StringComparison.Ordinal))
                        failures.Add($"sensitive content leaked through {measurement.MeterName}:{measurement.InstrumentName}");
                }
            }
        }
    }

    private static void ValidateMetrics(
        CapturedMeasurement[] measurements,
        IEnumerable<string> publishedMeterNames,
        ICollection<string> failures)
    {
        var meterNames = publishedMeterNames.ToArray();
        Require(meterNames.Contains(ExtensionsSourceName, StringComparer.Ordinal), "MEAI meter was not published", failures);
        Require(meterNames.Contains(AgentsSourceName, StringComparer.Ordinal), "Agent Framework meter was not published", failures);
        Require(
            meterNames.All(static name => !name.Contains("Agents.AI.Workflows", StringComparison.Ordinal)),
            "Workflow unexpectedly published a meter instrument",
            failures);

        RequireMetric(measurements, ExtensionsSourceName, OperationDuration, 2, failures);
        RequireMetric(measurements, ExtensionsSourceName, TokenUsage, 4, failures);
        RequireMetric(measurements, ExtensionsSourceName, TimeToFirstChunk, 1, failures);
        RequireMetric(measurements, ExtensionsSourceName, TimePerOutputChunk, 1, failures);
        RequireMetric(measurements, AgentsSourceName, OperationDuration, 2, failures);
        RequireMetric(measurements, AgentsSourceName, TokenUsage, 4, failures);
        RequireTokenValues(measurements, ExtensionsSourceName, failures);
        RequireTokenValues(measurements, AgentsSourceName, failures);
        Require(
            measurements.Length == 14,
            $"expected exactly 14 GenAI measurements, got {measurements.Length.ToString(CultureInfo.InvariantCulture)}",
            failures);

        string[] allowedMetricTags =
        [
            "error.type",
            "gen_ai.operation.name",
            "gen_ai.provider.name",
            "gen_ai.request.model",
            "gen_ai.response.model",
            "gen_ai.token.type",
            "server.address",
            "server.port",
        ];
        foreach (var measurement in measurements)
        {
            RequireTag(measurement, "gen_ai.operation.name", "chat", failures);
            RequireTag(measurement, "gen_ai.request.model", FixedModel, failures);
            RequireTag(measurement, "gen_ai.provider.name", FixedProvider, failures);
            foreach (var key in measurement.Tags.Keys)
            {
                if (!allowedMetricTags.Contains(key, StringComparer.Ordinal))
                    failures.Add($"unexpected metric dimension {measurement.MeterName}:{measurement.InstrumentName}:{key}");
            }
        }
    }

    private static void ValidateChatSpan(
        CapturedActivity span,
        ICollection<string> failures,
        string operation = "chat")
    {
        Require(
            StringComparer.Ordinal.Equals(span.Kind, ActivityKind.Client.ToString()),
            $"expected Client activity kind for {span.SourceName}:{span.Name}, got {span.Kind}",
            failures);
        RequireTag(span, "gen_ai.operation.name", operation, failures);
        RequireTag(span, "gen_ai.request.model", FixedModel, failures);
        RequireTag(span, "gen_ai.response.model", FixedModel, failures);
        RequireTag(span, "gen_ai.provider.name", FixedProvider, failures);
        RequireTag(span, "gen_ai.usage.input_tokens", "7", failures);
        RequireTag(span, "gen_ai.usage.output_tokens", "3", failures);
    }

    private static void RequireActivity(
        IEnumerable<CapturedActivity> activities,
        string name,
        ICollection<string> failures)
        => Require(
            activities.Any(activity => StringComparer.Ordinal.Equals(activity.Name, name)),
            $"missing workflow activity {name}",
            failures);

    private static void RequireMetric(
        IEnumerable<CapturedMeasurement> measurements,
        string meterName,
        string instrumentName,
        int expectedCount,
        ICollection<string> failures)
    {
        var count = measurements.Count(measurement =>
            StringComparer.Ordinal.Equals(measurement.MeterName, meterName) &&
            StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName));
        Require(
            count == expectedCount,
            $"expected {expectedCount.ToString(CultureInfo.InvariantCulture)} measurements for {meterName}:{instrumentName}, got {count.ToString(CultureInfo.InvariantCulture)}",
            failures);
    }

    private static void RequireTokenValues(
        IEnumerable<CapturedMeasurement> measurements,
        string meterName,
        ICollection<string> failures)
    {
        var tokens = measurements
            .Where(measurement =>
                StringComparer.Ordinal.Equals(measurement.MeterName, meterName) &&
                StringComparer.Ordinal.Equals(measurement.InstrumentName, TokenUsage))
            .ToArray();
        Require(
            tokens.Any(static measurement => measurement.Value == "7" && HasTag(measurement, "gen_ai.token.type", "input")),
            $"missing bounded input token measurement for {meterName}",
            failures);
        Require(
            tokens.Any(static measurement => measurement.Value == "3" && HasTag(measurement, "gen_ai.token.type", "output")),
            $"missing bounded output token measurement for {meterName}",
            failures);
    }

    private static void RequireTag(
        CapturedActivity activity,
        string key,
        string expected,
        ICollection<string> failures)
    {
        if (!HasTag(activity, key, expected))
        {
            activity.Tags.TryGetValue(key, out var actual);
            failures.Add($"expected {activity.SourceName}:{activity.Name}:{key}={expected}, got {actual ?? "<missing>"}");
        }
    }

    private static void RequireTag(
        CapturedMeasurement measurement,
        string key,
        string expected,
        ICollection<string> failures)
    {
        if (!HasTag(measurement, key, expected))
        {
            measurement.Tags.TryGetValue(key, out var actual);
            failures.Add($"expected {measurement.MeterName}:{measurement.InstrumentName}:{key}={expected}, got {actual ?? "<missing>"}");
        }
    }

    private static bool HasTag(CapturedActivity activity, string key, string expected)
        => activity.Tags.TryGetValue(key, out var actual) && StringComparer.Ordinal.Equals(actual, expected);

    private static bool HasTag(CapturedMeasurement measurement, string key, string expected)
        => measurement.Tags.TryGetValue(key, out var actual) && StringComparer.Ordinal.Equals(actual, expected);

    private static void Require(bool condition, string failure, ICollection<string> failures)
    {
        if (!condition)
            failures.Add(failure);
    }
}

internal static class ProbeContract
{
    internal const string ExtensionsSourceName = "Experimental.Microsoft.Extensions.AI";
    internal const string AgentsSourceName = "Experimental.Microsoft.Agents.AI";
    internal const string WorkflowsSourceName = "Microsoft.Agents.AI.Workflows";
    internal const string FixedModel = "fixed-model";
    internal const string FixedProvider = "fixed-provider";
    internal const string UserSensitiveContent = "private-user-content";
    internal const string AssistantSensitiveContent = "private-assistant-content";
    internal const string WorkflowSensitiveContent = "private-workflow-content";
}

internal static class TelemetryValue
{
    public static string Format(object? value)
        => value switch
        {
            null => string.Empty,
            string text => text,
            IEnumerable<string> values => string.Join("|", values),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
}

[JsonSerializable(typeof(GenAiReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealGenAiJsonContext : JsonSerializerContext;
