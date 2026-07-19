using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using Qyl;

const string mcpSourceName = "Experimental.ModelContextProtocol";
const string rootSourceName = "Qyl.RealMcpDemo";
const string sessionId = "qyl-mcp-evidence-session";

var exportedActivities = new ActivityExportCollection();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealMcpDemo",
    DisableDefaults = true,
});
builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-mcp-demo";
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.AdditionalSources.Add(rootSourceName);
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "qyl-real-mcp-server",
            Version = "1.0.0",
        };
    })
    .WithTools<ProbeTools>();

using var host = builder.Build();
await host.StartAsync();

var clientToServer = new Pipe();
var serverToClient = new Pipe();
await using var serverInput = clientToServer.Reader.AsStream();
await using var serverOutput = serverToClient.Writer.AsStream();
await using var clientWrite = clientToServer.Writer.AsStream();
await using var clientRead = serverToClient.Reader.AsStream();

var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
var serverOptions = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
await using var serverTransport = new StreamServerTransport(
    serverInput,
    serverOutput,
    "qyl-real-mcp-server",
    loggerFactory);
await using var server = McpServer.Create(serverTransport, serverOptions, loggerFactory, host.Services);
using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var serverTask = server.RunAsync(serverCancellation.Token);

string rootTraceId;
string rootSpanId;
var toolListed = false;
var toolCalled = false;
using (var rootSource = new ActivitySource(rootSourceName))
using (var root = rootSource.StartActivity("mcp evidence", ActivityKind.Internal)
                  ?? throw new InvalidOperationException("qyl did not register the demo root ActivitySource"))
{
    root.SetTag("session.id", sessionId);
    rootTraceId = root.TraceId.ToString();
    rootSpanId = root.SpanId.ToString();

    var clientTransport = new StreamClientTransport(clientWrite, clientRead, loggerFactory);
    await using var client = await McpClient.CreateAsync(clientTransport, cancellationToken: serverCancellation.Token);

    var tools = await client.ListToolsAsync(cancellationToken: serverCancellation.Token);
    toolListed = tools.Count == 1 && StringComparer.Ordinal.Equals(tools[0].Name, ProbeTools.ToolName);

    var callResult = await client.CallToolAsync(
        new CallToolRequestParams
        {
            Name = ProbeTools.ToolName,
            Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["secret"] = JsonSerializer.SerializeToElement(
                    ProbeTools.SensitiveArgument,
                    RealMcpJsonContext.Default.String),
            },
        },
        serverCancellation.Token);
    toolCalled = callResult.IsError is not true
                 && callResult.Content.Count == 1
                 && callResult.Content[0] is TextContentBlock text
                 && StringComparer.Ordinal.Equals(text.Text, ProbeTools.SensitiveResult);
}

await serverCancellation.CancelAsync();
await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
await host.StopAsync();

var activities = exportedActivities.Snapshot()
    .Where(activity => StringComparer.Ordinal.Equals(activity.Source.Name, mcpSourceName))
    .Select(CapturedActivity.From)
    .OrderBy(static activity => activity.Name, StringComparer.Ordinal)
    .ThenBy(static activity => activity.Kind, StringComparer.Ordinal)
    .ToArray();
var registrationEnabled = !string.Equals(
    Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_TRACES_MCP_INSTRUMENTATION_ENABLED"),
    "false",
    StringComparison.OrdinalIgnoreCase);
var report = McpReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    toolListed,
    toolCalled,
    registrationEnabled,
    rootTraceId,
    rootSpanId,
    activities);

Console.WriteLine(JsonSerializer.Serialize(report, RealMcpJsonContext.Default.McpReport));
return report.Pass ? 0 : 1;

[McpServerToolType]
internal sealed class ProbeTools
{
    internal const string ToolName = "qyl_sensitive_probe";
    internal const string SensitiveArgument = "mcp-sensitive-argument-73f8a9";
    internal const string SensitiveResult = "mcp-sensitive-result-9d20c4";

    [McpServerTool(Name = ToolName, ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns a deterministic private result after validating a private argument.")]
    public string Probe(
        [Description("A private value used to prove that MCP telemetry does not capture tool arguments.")]
        string secret)
        => StringComparer.Ordinal.Equals(secret, SensitiveArgument)
            ? SensitiveResult
            : throw new ArgumentException("The probe argument was not the expected value.", nameof(secret));
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    string? StatusDescription,
    string TraceId,
    string SpanId,
    string ParentSpanId,
    IReadOnlyDictionary<string, string> Tags)
{
    internal static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.StatusDescription,
            activity.TraceId.ToString(),
            activity.SpanId.ToString(),
            activity.ParentSpanId.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record McpReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    bool ToolListed,
    bool ToolCalled,
    bool QylRegistrationEnabled,
    string RootTraceId,
    string RootSpanId,
    CapturedActivity[] Activities)
{
    internal static McpReport Create(
        string runtimeMode,
        bool toolListed,
        bool toolCalled,
        bool registrationEnabled,
        string rootTraceId,
        string rootSpanId,
        CapturedActivity[] activities)
    {
        var failures = new List<string>();
        if (!toolListed)
            failures.Add("the client did not list exactly the rooted probe tool");
        if (!toolCalled)
            failures.Add("the rooted probe tool did not return its deterministic result");
        var expectedCount = registrationEnabled ? 8 : 0;
        if (activities.Length != expectedCount)
            failures.Add($"expected {expectedCount} MCP activities, got {activities.Length.ToString(CultureInfo.InvariantCulture)}");

        if (!registrationEnabled)
        {
            return new McpReport(
                runtimeMode,
                failures.Count is 0,
                failures.ToArray(),
                toolListed,
                toolCalled,
                false,
                rootTraceId,
                rootSpanId,
                activities);
        }

        RequireOperationPair(activities, "initialize", "initialize", rootTraceId, rootSpanId, failures);
        RequireOperationPair(
            activities,
            "notifications/initialized",
            "notifications/initialized",
            rootTraceId,
            rootSpanId,
            failures);
        RequireOperationPair(activities, "tools/list", "tools/list", rootTraceId, rootSpanId, failures);
        RequireOperationPair(
            activities,
            $"tools/call {ProbeTools.ToolName}",
            "tools/call",
            rootTraceId,
            rootSpanId,
            failures);

        RequireStableSession(activities, "Client", failures);
        RequireStableSession(activities, "Server", failures);

        foreach (var activity in activities)
        {
            if (!StringComparer.Ordinal.Equals(activity.Source, "Experimental.ModelContextProtocol"))
                failures.Add($"unexpected MCP ActivitySource: {activity.Source}");
            if (!StringComparer.Ordinal.Equals(activity.Status, "Unset"))
                failures.Add($"expected successful status for {activity.Name}/{activity.Kind}, got {activity.Status}");
            RequireTag(activity, "network.transport", "pipe", failures);

            if (ContainsSensitiveValue(activity))
                failures.Add($"sensitive tool argument or result leaked into {activity.Name}/{activity.Kind}");
        }

        var toolActivities = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Name,
                $"tools/call {ProbeTools.ToolName}"))
            .ToArray();
        foreach (var activity in toolActivities)
        {
            RequireTag(activity, "gen_ai.tool.name", ProbeTools.ToolName, failures);
            RequireTag(activity, "gen_ai.operation.name", "execute_tool", failures);
        }

        return new McpReport(
            runtimeMode,
            failures.Count is 0,
            failures.ToArray(),
            toolListed,
            toolCalled,
            true,
            rootTraceId,
            rootSpanId,
            activities);
    }

    private static void RequireOperationPair(
        CapturedActivity[] activities,
        string activityName,
        string method,
        string rootTraceId,
        string rootSpanId,
        ICollection<string> failures)
    {
        var pair = activities
            .Where(activity => StringComparer.Ordinal.Equals(activity.Name, activityName))
            .ToArray();
        if (pair.Length != 2)
        {
            failures.Add($"expected one client and one server activity for {activityName}, got {pair.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        var client = pair.SingleOrDefault(static activity => StringComparer.Ordinal.Equals(activity.Kind, "Client"));
        var server = pair.SingleOrDefault(static activity => StringComparer.Ordinal.Equals(activity.Kind, "Server"));
        if (client is null || server is null)
        {
            failures.Add($"missing client/server activity pair for {activityName}");
            return;
        }

        RequireTag(client, "mcp.method.name", method, failures);
        RequireTag(server, "mcp.method.name", method, failures);
        if (!StringComparer.Ordinal.Equals(client.TraceId, rootTraceId)
            || !StringComparer.Ordinal.Equals(client.ParentSpanId, rootSpanId))
        {
            failures.Add($"client {activityName} was not parented by the qyl evidence root");
        }

        if (!StringComparer.Ordinal.Equals(server.TraceId, client.TraceId)
            || !StringComparer.Ordinal.Equals(server.ParentSpanId, client.SpanId))
        {
            failures.Add($"server {activityName} did not continue the client trace context");
        }

        RequireTag(client, "session.id", "qyl-mcp-evidence-session", failures);
        if (!StringComparer.Ordinal.Equals(method, "initialize"))
        {
            RequireTag(client, "mcp.protocol.version", "2025-11-25", failures);
            RequireTag(server, "mcp.protocol.version", "2025-11-25", failures);
        }
    }

    private static void RequireStableSession(
        IEnumerable<CapturedActivity> activities,
        string kind,
        ICollection<string> failures)
    {
        var sessionIds = activities
            .Where(activity => StringComparer.Ordinal.Equals(activity.Kind, kind))
            .Select(activity => activity.Tags.GetValueOrDefault("mcp.session.id"))
            .Where(static value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (sessionIds.Length != 1)
            failures.Add($"expected one stable {kind} MCP session identifier, got {sessionIds.Length.ToString(CultureInfo.InvariantCulture)}");
    }

    private static bool ContainsSensitiveValue(CapturedActivity activity)
    {
        if (ContainsSensitiveValue(activity.Name) || ContainsSensitiveValue(activity.StatusDescription))
            return true;

        return activity.Tags.Any(static tag =>
            ContainsSensitiveValue(tag.Key) || ContainsSensitiveValue(tag.Value));
    }

    private static bool ContainsSensitiveValue(string? value)
        => value?.Contains(ProbeTools.SensitiveArgument, StringComparison.Ordinal) is true
           || value?.Contains(ProbeTools.SensitiveResult, StringComparison.Ordinal) is true;

    private static void RequireTag(
        CapturedActivity activity,
        string key,
        string expected,
        ICollection<string> failures)
    {
        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on {activity.Name}/{activity.Kind}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected} on {activity.Name}/{activity.Kind}, got {actual}");
    }
}

[JsonSerializable(typeof(McpReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMcpJsonContext : JsonSerializerContext;

internal sealed class ActivityExportCollection : ICollection<Activity>
{
    private readonly Lock _gate = new();
    private readonly List<Activity> _items = [];

    public int Count
    {
        get
        {
            lock (_gate)
                return _items.Count;
        }
    }

    public bool IsReadOnly => false;

    public void Add(Activity item)
    {
        lock (_gate)
            _items.Add(item);
    }

    public void Clear()
    {
        lock (_gate)
            _items.Clear();
    }

    public bool Contains(Activity item)
    {
        lock (_gate)
            return _items.Contains(item);
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_gate)
            _items.CopyTo(array, arrayIndex);
    }

    public bool Remove(Activity item)
    {
        lock (_gate)
            return _items.Remove(item);
    }

    public IEnumerator<Activity> GetEnumerator()
        => ((IEnumerable<Activity>)Snapshot()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal Activity[] Snapshot()
    {
        lock (_gate)
            return _items.ToArray();
    }
}
