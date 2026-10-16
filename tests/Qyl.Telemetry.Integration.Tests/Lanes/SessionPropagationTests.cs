using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Qyl.Telemetry.Testing;
using SessionAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Session.SessionAttributes;

namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// Runs the session-propagation demo as TWO REAL PROCESSES and holds qyl to its own contract:
/// (1) an in-process descendant of a span carrying session.id inherits it, (2) a descendant that
/// already carries its own is not overwritten, (3) across a real HTTP boundary into a second process
/// the server span carries the session.id that reached it in baggage — stamped by AddQyl's ASP.NET Core
/// middleware, no Qyl.Api involved — on the same trace with a remote parent, and its in-process child
/// inherits it. This lane, never the demo binary, starts both processes, and each report has to name
/// the process id the lane launched. The downstream role's locally rooted control proves its processor
/// works on a span that no baggage reached.
/// </summary>
[TestClass]
[TestCategory("session-propagation")]
public sealed class SessionPropagationTests(TestContext testContext)
{
    private const string DemoSource = "Qyl.Telemetry.Application";
    private const string AspNetCoreSource = "Microsoft.AspNetCore";
    private const string HttpClientSource = "System.Net.Http";
    private const string SessionTag = SessionAttributes.Id;
    private const string SessionId = "qyl-real-session-propagation-demo-session";
    private const string OwnSessionId = "qyl-real-session-propagation-demo-own-session";
    private const string DownstreamSessionId = "qyl-real-session-propagation-demo-downstream-session";
    private const string EmptySpanId = "0000000000000000";

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("session-propagation"), testContext);
        lane.BuildManaged();
        var port = Shell.FreeTcpPort();
        await using var receiver = await OtlpReceiver.StartAsync(testContext.CancellationToken);
        var environment = lane.With(("QYL_ENDPOINT", receiver.Endpoint.ToString().TrimEnd('/')));
        ProcessResult upstream;
        ProcessResult downstream;
        using (var downstreamProcess = lane.StartManaged(["--role", "downstream", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)], environment))
        {
            WaitForPort(port, downstreamProcess);
            using var upstreamProcess = lane.StartManaged(["--role", "upstream", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture)], environment);
            upstream = await upstreamProcess.WaitForExitAsync(TimeSpan.FromSeconds(120), testContext.CancellationToken);
            // The downstream stops itself once the one probe has been answered.
            downstream = await downstreamProcess.WaitForExitAsync(TimeSpan.FromSeconds(60), testContext.CancellationToken);
        }

        var snapshot = receiver.Snapshot();
        lane.RecordCapture("two-process session propagation", snapshot);
        Assert.IsTrue(upstream.Stdout.Contains("probe-status=204", StringComparison.Ordinal), $"upstream did not reach the downstream probe\nstdout={upstream.Stdout}");
        var up = VerifyProcess(new DemoRun("upstream demo", LaneRuntime.Managed, upstream, snapshot), "upstream");
        var down = VerifyProcess(new DemoRun("downstream demo", LaneRuntime.Managed, downstream, snapshot), "downstream");
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
            Assert.IsTrue(!process.HasExited, $"downstream exited before it listened\nstdout={process.Stdout}\nstderr={process.Stderr}");
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

        throw new AssertFailedException($"downstream did not listen on 127.0.0.1:{port} within 30s");
    }

    private static DemoRun VerifyProcess(DemoRun run, string role)
    {
        run.RequireSucceeded();
        Assert.IsTrue(run.StringField("Role") == role, $"{run.Label} reported role {run.StringField("Role")}, expected {role}");
        Assert.IsTrue(run.StringField("RuntimeMode") == "dynamic-code-supported", $"{run.Label} runtime mode mismatch: {run.StringField("RuntimeMode")}");
        Assert.IsTrue(run.BoolField("SessionTagInheritanceEnabled") == true, $"{run.Label} ran with session-tag inheritance off — the lane would prove nothing");
        run.RequirePass();
        // A shared in-memory exporter, or one process reporting for both roles, dies here.
        var reported = run.Field("ProcessId");
        Assert.IsTrue(reported.ValueKind is JsonValueKind.Number && reported.GetInt32() == run.Process.ProcessId,
            $"{run.Label} reported process id {reported}, but was launched as {run.Process.ProcessId}");
        return run;
    }

    private static JsonElement Span(DemoRun run, string source, string name)
    {
        var matches = run.Array("Activities").EnumerateArray()
            .Where(activity => ReportedActivity.Text(activity, "Source") == source && ReportedActivity.Text(activity, "Name") == name)
            .ToArray();
        Assert.IsTrue(matches.Length == 1, $"{run.Label} exported {matches.Length} spans named {source}/{name}, expected exactly 1");
        return matches[0];
    }

    private static void RequireTag(DemoRun run, JsonElement span, string expected)
    {
        var actual = ReportedActivity.Tag(span, SessionTag);
        Assert.IsTrue(actual == expected, $"{run.Label} span '{ReportedActivity.Text(span, "Name")}' expected {SessionTag}={expected}, got {actual ?? "absent"}");
    }

    private static void VerifyContract(DemoRun upstream, DemoRun downstream)
    {
        // Two processes, not two hosts.
        Assert.IsTrue(upstream.Process.ProcessId != downstream.Process.ProcessId, $"both roles reported process id {upstream.Process.ProcessId} — they ran in one process");

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
        Assert.IsTrue(inherited.TryGetProperty("HasInProcessParent", out var inheritedParent) && inheritedParent.ValueKind is JsonValueKind.True,
            "upstream 'inherits session' span has no in-process parent — it inherited from nothing");

        // PROPERTY 2 — a descendant that brought its own session keeps it.
        RequireTag(upstream, own, OwnSessionId);

        // The BCL's client span is an in-process descendant like any other.
        RequireTag(upstream, client, SessionId);
        Assert.IsTrue(ReportedActivity.Text(client, "TraceId") == ReportedActivity.Text(root, "TraceId"),
            $"upstream client span is on trace {ReportedActivity.Text(client, "TraceId")}, root is on {ReportedActivity.Text(root, "TraceId")}");

        // PROPERTY 3 — one trace, a real remote parent, and the baggage session stamped on the server span.
        Assert.IsTrue(ReportedActivity.Text(server, "TraceId") == ReportedActivity.Text(client, "TraceId"),
            "downstream server span and upstream client span are not on one trace — the two processes are not one trace");
        Assert.IsTrue(ReportedActivity.Text(server, "ParentSpanId") == ReportedActivity.Text(client, "SpanId"),
            $"downstream server span parents to {ReportedActivity.Text(server, "ParentSpanId")}, the upstream client span is {ReportedActivity.Text(client, "SpanId")} — traceparent did not cross the boundary");
        Assert.IsTrue(ReportedActivity.Text(server, "ParentSpanId") != EmptySpanId, "downstream server span has no remote parent at all");
        Assert.IsTrue(server.TryGetProperty("HasInProcessParent", out var serverParent) && serverParent.ValueKind is JsonValueKind.False,
            "downstream server span has an IN-PROCESS parent — the roles did not run in two processes");

        // The session reached the second process in baggage, which the application put there, and
        // AddQyl stamped it onto the server span; the server span's in-process child inherits it.
        var baggage = server.TryGetProperty("Baggage", out var map) && map.ValueKind is JsonValueKind.Object && map.TryGetProperty(SessionTag, out var value) ? value.GetString() : null;
        Assert.IsTrue(baggage == SessionId, $"downstream span 'GET /probe' expected baggage {SessionTag}={SessionId}, got {baggage ?? "absent"}");
        RequireTag(downstream, server, SessionId);
        RequireTag(downstream, probeChild, SessionId);

        // The control: propagation is installed and working in the downstream process.
        RequireTag(downstream, localRoot, DownstreamSessionId);
        RequireTag(downstream, localChild, DownstreamSessionId);
        Assert.IsTrue(localChild.TryGetProperty("HasInProcessParent", out var localParent) && localParent.ValueKind is JsonValueKind.True,
            "downstream control child has no in-process parent — the control proves nothing");
        Assert.IsTrue(ReportedActivity.Text(localChild, "TraceId") != ReportedActivity.Text(server, "TraceId"),
            "downstream control span shares the request trace — it is not the locally rooted control");
    }
}
