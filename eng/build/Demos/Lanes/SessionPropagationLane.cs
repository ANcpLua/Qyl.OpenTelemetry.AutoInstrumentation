using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SessionAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Session.SessionAttributes;

namespace Qyl.Build;

/// <summary>
/// Runs the session-propagation demo as TWO REAL PROCESSES and holds qyl to its own contract:
/// (1) an in-process descendant of a span carrying session.id inherits it, (2) a descendant that
/// already carries its own is not overwritten, (3) across a real HTTP boundary into a second process
/// the server span carries NO session.id, although traceparent arrived, the trace is the same one and
/// the session reached that span in baggage. This lane, never the demo binary, starts both processes,
/// and each report has to name the process id the lane launched. The downstream role's locally rooted
/// control proves its processor works, so (3) measures the boundary and not a missing processor.
/// </summary>
sealed class SessionPropagationLane : DemoLane
{
    private const string DemoSource = "Qyl.RealSessionPropagationDemo";
    private const string AspNetCoreSource = "Microsoft.AspNetCore";
    private const string HttpClientSource = "System.Net.Http";
    private const string SessionTag = SessionAttributes.Id;
    private const string SessionId = "qyl-real-session-propagation-demo-session";
    private const string OwnSessionId = "qyl-real-session-propagation-demo-own-session";
    private const string DownstreamSessionId = "qyl-real-session-propagation-demo-downstream-session";
    private const string EmptySpanId = "0000000000000000";

    public override string Name => "session-propagation";

    public override string Demo => "Qyl.RealSessionPropagationDemo";

    public override bool RunsNativeAot => false;

    public override string ServiceName => "qyl-real-session-propagation-demo-upstream";

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var port = QylCollector.FreePort();
        var started = DateTimeOffset.UtcNow;
        ProcessResult upstream;
        ProcessResult downstream;
        using (var downstreamProcess = lane.StartManaged(["--role", "downstream", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            WaitForPort(port, downstreamProcess);
            using var upstreamProcess = lane.StartManaged(["--role", "upstream", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            upstream = upstreamProcess.WaitForExit(TimeSpan.FromSeconds(120));
            // The downstream stops itself once the one probe has been answered.
            downstream = downstreamProcess.WaitForExit(TimeSpan.FromSeconds(60));
        }

        var finished = DateTimeOffset.UtcNow;
        Gate.Require(upstream.Stdout.Contains("probe-status=204", StringComparison.Ordinal), $"upstream did not reach the downstream probe\nstdout={upstream.Stdout}");
        var up = VerifyProcess(new DemoRun("upstream demo", LaneRuntime.Managed, upstream, started, finished), "upstream");
        var down = VerifyProcess(new DemoRun("downstream demo", LaneRuntime.Managed, downstream, started, finished), "downstream");
        VerifyContract(up, down);

        // Both processes exported over OTLP; each span the two reports name arrived as reported.
        lane.Telemetry.Spans(up, up.Array("Activities"), "upstream");
        lane.Telemetry.Spans(down, down.Array("Activities"), "downstream");
    }

    private static void WaitForPort(int port, RunningProcess process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            Gate.Require(!process.HasExited, () => $"downstream exited before it listened\nstdout={process.Stdout}\nstderr={process.Stderr}");
            try
            {
                using var probe = new TcpClient();
                probe.Connect(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(50);
            }
        }

        throw Gate.Fail($"downstream did not listen on 127.0.0.1:{port} within 30s");
    }

    private static DemoRun VerifyProcess(DemoRun run, string role)
    {
        run.RequireSucceeded();
        Gate.Require(run.StringField("Role") == role, $"{run.Label} reported role {run.StringField("Role")}, expected {role}");
        Gate.Require(run.StringField("RuntimeMode") == "dynamic-code-supported", $"{run.Label} runtime mode mismatch: {run.StringField("RuntimeMode")}");
        Gate.Require(run.BoolField("SessionTagInheritanceEnabled") == true, $"{run.Label} ran with session-tag inheritance off — the lane would prove nothing");
        run.RequirePass();
        // A shared in-memory exporter, or one process reporting for both roles, dies here.
        var reported = run.Field("ProcessId");
        Gate.Require(reported.ValueKind is JsonValueKind.Number && reported.GetInt32() == run.Process.ProcessId,
            $"{run.Label} reported process id {reported}, but was launched as {run.Process.ProcessId}");
        return run;
    }

    private static JsonElement Span(DemoRun run, string source, string name)
    {
        var matches = run.Array("Activities").EnumerateArray()
            .Where(activity => ReportedActivity.Text(activity, "Source") == source && ReportedActivity.Text(activity, "Name") == name)
            .ToArray();
        Gate.Require(matches.Length == 1, $"{run.Label} exported {matches.Length} spans named {source}/{name}, expected exactly 1");
        return matches[0];
    }

    private static void RequireTag(DemoRun run, JsonElement span, string expected)
    {
        var actual = ReportedActivity.Tag(span, SessionTag);
        Gate.Require(actual == expected, $"{run.Label} span '{ReportedActivity.Text(span, "Name")}' expected {SessionTag}={expected}, got {actual ?? "absent"}");
    }

    private static void RequireNoTag(DemoRun run, JsonElement span)
    {
        Gate.Require(!ReportedActivity.HasTag(span, SessionTag),
            $"{run.Label} span '{ReportedActivity.Text(span, "Name")}' carries {SessionTag}={ReportedActivity.Tag(span, SessionTag)} across a process boundary — qyl copies the session onto a remote-parented span");
    }

    private static void VerifyContract(DemoRun upstream, DemoRun downstream)
    {
        // Two processes, not two hosts.
        Gate.Require(upstream.Process.ProcessId != downstream.Process.ProcessId, $"both roles reported process id {upstream.Process.ProcessId} — they ran in one process");

        var root = Span(upstream, DemoSource, "upstream root");
        var inherited = Span(upstream, DemoSource, "upstream inherits session");
        var own = Span(upstream, DemoSource, "upstream owns session");
        var client = Span(upstream, HttpClientSource, "GET");
        var server = Span(downstream, AspNetCoreSource, "GET /probe");
        var probeChild = Span(downstream, DemoSource, "downstream probe child");
        var localRoot = Span(downstream, DemoSource, "downstream local root");
        var localChild = Span(downstream, DemoSource, "downstream local child");

        RequireTag(upstream, root, SessionId);

        // PROPERTY 1 — an in-process descendant inherits the ancestor's session, by value.
        RequireTag(upstream, inherited, SessionId);
        Gate.Require(inherited.TryGetProperty("HasInProcessParent", out var inheritedParent) && inheritedParent.ValueKind is JsonValueKind.True,
            "upstream 'inherits session' span has no in-process parent — it inherited from nothing");

        // PROPERTY 2 — a descendant that brought its own session keeps it.
        RequireTag(upstream, own, OwnSessionId);

        // The BCL's client span is an in-process descendant like any other.
        RequireTag(upstream, client, SessionId);
        Gate.Require(ReportedActivity.Text(client, "TraceId") == ReportedActivity.Text(root, "TraceId"),
            $"upstream client span is on trace {ReportedActivity.Text(client, "TraceId")}, root is on {ReportedActivity.Text(root, "TraceId")}");

        // PROPERTY 3 — one trace, a real remote parent, and no session on the server span.
        Gate.Require(ReportedActivity.Text(server, "TraceId") == ReportedActivity.Text(client, "TraceId"),
            "downstream server span and upstream client span are not on one trace — the two processes are not one trace");
        Gate.Require(ReportedActivity.Text(server, "ParentSpanId") == ReportedActivity.Text(client, "SpanId"),
            $"downstream server span parents to {ReportedActivity.Text(server, "ParentSpanId")}, the upstream client span is {ReportedActivity.Text(client, "SpanId")} — traceparent did not cross the boundary");
        Gate.Require(ReportedActivity.Text(server, "ParentSpanId") != EmptySpanId, "downstream server span has no remote parent at all");
        Gate.Require(server.TryGetProperty("HasInProcessParent", out var serverParent) && serverParent.ValueKind is JsonValueKind.False,
            "downstream server span has an IN-PROCESS parent — the roles did not run in two processes");
        RequireNoTag(downstream, server);

        // The session reached the second process in baggage, which the application put there. qyl wrote no tag.
        var baggage = server.TryGetProperty("Baggage", out var map) && map.ValueKind is JsonValueKind.Object && map.TryGetProperty(SessionTag, out var value) ? value.GetString() : null;
        Gate.Require(baggage == SessionId, $"downstream span 'GET /probe' expected baggage {SessionTag}={SessionId}, got {baggage ?? "absent"}");
        RequireNoTag(downstream, probeChild);

        // The control: propagation is installed and working in the downstream process.
        RequireTag(downstream, localRoot, DownstreamSessionId);
        RequireTag(downstream, localChild, DownstreamSessionId);
        Gate.Require(localChild.TryGetProperty("HasInProcessParent", out var localParent) && localParent.ValueKind is JsonValueKind.True,
            "downstream control child has no in-process parent — the control proves nothing");
        Gate.Require(ReportedActivity.Text(localChild, "TraceId") != ReportedActivity.Text(server, "TraceId"),
            "downstream control span shares the request trace — it is not the locally rooted control");
    }
}
