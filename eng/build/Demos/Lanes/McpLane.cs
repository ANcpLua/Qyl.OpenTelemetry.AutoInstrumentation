using System.Text.Json;

namespace Qyl.Build;

/// <summary>
/// The official ModelContextProtocol client/server sources over a pipe: discovery, tools/list and a
/// tools/call, each a client/server pair on one trace under the qyl root, with stable and distinct
/// session ids and no sensitive tool value in the output. The registration on and toggled off,
/// managed and NativeAOT.
/// </summary>
sealed class McpLane : DemoLane
{
    private const string McpSource = "Experimental.ModelContextProtocol";
    private static readonly string[] s_sensitiveValues = ["mcp-sensitive-argument-73f8a9", "mcp-sensitive-result-9d20c4"];

    private static readonly Dictionary<string, string> s_operations = new(StringComparer.Ordinal)
    {
        ["server/discover"] = "server/discover",
        ["tools/list"] = "tools/list",
        ["tools/call qyl_sensitive_probe"] = "tools/call",
    };

    public override string Name => "mcp";

    public override string Demo => "Qyl.RealMcpDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        var disabled = lane.With(("OTEL_DOTNET_AUTO_TRACES_MCP_INSTRUMENTATION_ENABLED", "false"));
        lane.BuildManaged("--disable-build-servers");
        Verify(lane, lane.RunManaged("managed MCP demo"), registrationEnabled: true);
        Verify(lane, lane.RunManaged("managed MCP demo (registration disabled)", disabled), registrationEnabled: false);

        var executable = lane.PublishNativeAot();
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT MCP demo"), registrationEnabled: true);
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT MCP demo (registration disabled)", disabled), registrationEnabled: false);
    }

    private static void Verify(LaneContext lane, DemoRun run, bool registrationEnabled)
    {
        run.RequireSucceeded();
        foreach (var sensitive in s_sensitiveValues)
            Gate.Require(!run.Stdout.Contains(sensitive, StringComparison.Ordinal), $"{run.Label} leaked a sensitive tool value to telemetry output: {sensitive}");
        run.RequireReportPassed()
            .RequireBool("ToolListed", true)
            .RequireBool("ToolCalled", true)
            .RequireBool("QylRegistrationEnabled", registrationEnabled);

        var activities = run.RequireCount("Activities", registrationEnabled ? 6 : 0, "MCP activities");
        lane.Telemetry.Spans(run, activities, "MCP");
        if (!registrationEnabled)
            return;

        var all = activities.EnumerateArray().ToArray();
        Gate.Require(all.All(static activity => ReportedActivity.Text(activity, "Source") == McpSource), $"{run.Label} ActivitySource mismatch:\n{activities}");
        var names = all.GroupBy(static activity => ReportedActivity.Text(activity, "Name") ?? string.Empty).ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        Gate.Require(names.Count == s_operations.Count && s_operations.Keys.All(name => names.GetValueOrDefault(name) == 2), $"{run.Label} operation set mismatch:\n{activities}");

        var rootTrace = run.StringField("RootTraceId");
        var rootSpan = run.StringField("RootSpanId");
        Gate.Require(rootTrace is not null && rootSpan is not null, $"{run.Label} report is missing root context:\n{run.Dump()}");

        var sessions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["Client"] = [], ["Server"] = [] };
        foreach (var (operation, method) in s_operations)
        {
            var pair = all.Where(activity => ReportedActivity.Text(activity, "Name") == operation).ToArray();
            var byKind = pair.ToDictionary(static activity => ReportedActivity.Text(activity, "Kind") ?? string.Empty, StringComparer.Ordinal);
            Gate.Require(byKind.Count == 2 && byKind.ContainsKey("Client") && byKind.ContainsKey("Server"), $"{run.Label} activity pair mismatch for {operation}:\n{string.Join("\n", pair)}");
            var client = byKind["Client"];
            var server = byKind["Server"];
            RequireTags(run, client, ("mcp.method.name", method), ("network.transport", "pipe"), ("session.id", "qyl-mcp-evidence-session"));
            RequireTags(run, server, ("mcp.method.name", method), ("network.transport", "pipe"));

            Gate.Require(ReportedActivity.Text(client, "TraceId") == rootTrace && ReportedActivity.Text(client, "ParentSpanId") == rootSpan,
                $"{run.Label} client activity lost its qyl parent for {operation}: {client}");
            Gate.Require(ReportedActivity.Text(server, "TraceId") == ReportedActivity.Text(client, "TraceId") && ReportedActivity.Text(server, "ParentSpanId") == ReportedActivity.Text(client, "SpanId"),
                $"{run.Label} server activity did not continue client context for {operation}");

            foreach (var (kind, activity) in byKind)
            {
                var session = ReportedActivity.Tag(activity, "mcp.session.id");
                Gate.Require(!string.IsNullOrEmpty(session), $"{run.Label} {kind} activity is missing mcp.session.id: {activity}");
                sessions[kind].Add(session!);
            }
        }

        Gate.Require(sessions.Values.All(static values => values.Count == 1), $"{run.Label} session identifiers were not stable");
        Gate.Require(!sessions["Client"].SetEquals(sessions["Server"]), $"{run.Label} client/server sessions were not distinct");
        foreach (var activity in all.Where(static activity => ReportedActivity.Text(activity, "Name") == "tools/call qyl_sensitive_probe"))
            RequireTags(run, activity, ("gen_ai.tool.name", "qyl_sensitive_probe"), ("gen_ai.operation.name", "execute_tool"), ("mcp.protocol.version", "2026-07-28"));
    }

    private static void RequireTags(DemoRun run, JsonElement activity, params (string Key, string Value)[] expected)
    {
        foreach (var (key, value) in expected)
            Gate.Require(ReportedActivity.Tag(activity, key) == value, $"{run.Label} MCP activity tag mismatch for {key}: expected={value} activity={activity}");
    }
}
