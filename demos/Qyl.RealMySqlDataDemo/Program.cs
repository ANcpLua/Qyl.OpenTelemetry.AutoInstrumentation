using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using OpenTelemetry.Trace;
using Qyl;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

var connectionString = Environment.GetEnvironmentVariable("QYL_MYSQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_MYSQL_CONNECTION_STRING is required.");
    return 2;
}

// The readiness probe runs on its own unpooled connection string so it cannot leave a warm
// connection behind: the demo's first Open below has to be a physical connect, and a physical
// connect is what emits the driver's handshake spans.
await WaitForMySqlAsync(connectionString);

// The real registration path: AddQyl subscribes to MySql.Data's own ActivitySource
// ("connector-net") and installs the one native-span processor. There is no consumer opt-in to
// add and none to prove: MySql.Data.OpenTelemetry's AddConnectorNet() is literally
// AddSource("connector-net"), which AddQyl already does.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealMySqlDataDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-mysqldata-demo";
    // The live-check gate points this at its OTLP listener. Unset, the demo exports into a
    // closed port and asserts on its in-memory exporter alone.
    options.CollectorEndpoint =
        new Uri(Environment.GetEnvironmentVariable("QYL_LIVE_CHECK_ENDPOINT") ?? "http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

// The second listener is the point of this demo. It listens to EVERY ActivitySource in the
// process and snapshots each activity as it stops -- which is exactly what a span processor
// sees -- so the report can assert the exact span set a database operation produces rather than
// a filtered subset. A span from a source qyl never subscribed still lands here and still has to
// be accounted for.
var observedSpans = new List<CapturedActivity>();
var phase = MySqlDataReport.PhaseStartup;
using var activityListener = new ActivityListener
{
    ShouldListenTo = static _ => true,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => observedSpans.Add(CapturedActivity.From(activity, phase)),
};
ActivitySource.AddActivityListener(activityListener);

// A physical open: the driver runs its own handshake statements on the new connection, and starts
// the Connection activity that only stops at Close/Dispose.
phase = MySqlDataReport.PhasePhysicalOpen;
var connection = new MySqlConnection(connectionString);
connection.Open();
Console.WriteLine("physical-open=1");

phase = MySqlDataReport.PhaseSelect;
await using (var command = new MySqlCommand(MySqlDataReport.SelectStatement, connection))
{
    var scalar = command.ExecuteScalar();
    Console.WriteLine("select-scalar=" + Convert.ToString(scalar, CultureInfo.InvariantCulture));
}

phase = MySqlDataReport.PhaseFailingSelect;
await using (var command = new MySqlCommand(MySqlDataReport.FailingStatement, connection))
{
    try
    {
        _ = command.ExecuteScalar();
        Console.WriteLine("unexpected-mysqldata-success=1");
    }
    catch (MySqlException exception)
    {
        Console.WriteLine("expected-mysqldata-error=" + exception.GetType().FullName);
    }
}

// Disposing the connection is what stops the Connection activity and returns the connection to
// the pool.
phase = MySqlDataReport.PhaseClose;
connection.Dispose();

// The re-open takes the pooled connection, so there is no handshake to trace this time.
phase = MySqlDataReport.PhasePooledOpen;
var pooledConnection = new MySqlConnection(connectionString);
pooledConnection.Open();
Console.WriteLine("pooled-open=1");

phase = MySqlDataReport.PhasePooledClose;
pooledConnection.Dispose();

// The listener stops here, before the flush below. What the OTLP exporter does with the spans is
// the demo's own telemetry, not a database operation, and it would otherwise show up as
// System.Net.Http spans in the asserted set.
activityListener.Dispose();
phase = MySqlDataReport.PhaseShutdown;

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = MySqlDataReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    observedSpans,
    exportedActivities);

var json = JsonSerializer.Serialize(report, RealMySqlDataJsonContext.Default.MySqlDataReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForMySqlAsync(string connectionString)
{
    var probeBuilder = new MySqlConnectionStringBuilder(connectionString) { Pooling = false };
    Exception? lastException = null;

    for (var attempt = 0; attempt < 120; attempt++)
    {
        try
        {
            await using var probe = new MySqlConnection(probeBuilder.ConnectionString);
            await probe.OpenAsync();
            await using var command = new MySqlCommand("SELECT 1", probe);
            _ = await command.ExecuteScalarAsync();
            return;
        }
        catch (MySqlException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("MySQL did not become ready.", lastException);
}

/// <summary>A tag value and the CLR type the library actually put on the span.</summary>
internal sealed record CapturedTag(string Value, string ValueType);

internal sealed record CapturedEvent(string Name, IReadOnlyDictionary<string, string> Tags);

/// <summary>One activity as it stood the moment it stopped, tagged with the operation it belongs to.</summary>
internal sealed record CapturedActivity(
    string Phase,
    string Source,
    string Name,
    string Kind,
    string Status,
    string? StatusDescription,
    IReadOnlyDictionary<string, CapturedTag> Tags,
    CapturedEvent[] Events,
    string Id)
{
    public string Tuple => Source + "/" + Kind;

    public static CapturedActivity From(Activity activity, string phase)
        => new(
            phase,
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.StatusDescription,
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => new CapturedTag(
                    Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    tag.Value?.GetType().Name ?? "null"),
                StringComparer.Ordinal),
            [.. activity.Events.Select(static evt => new CapturedEvent(
                evt.Name,
                evt.Tags.ToDictionary(
                    static tag => tag.Key,
                    static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.Ordinal)))],
            activity.Id ?? string.Empty);
}

/// <summary>The exact sorted multiset of (source, kind) tuples one database operation must produce.</summary>
internal sealed record PhaseSpanSet(string Phase, string[] Expected, string[] Actual);

/// <summary>
/// The <c>otel.status_code</c> of one span read twice: as the span stopped, and again after the
/// process is done with the database.
/// </summary>
internal sealed record StatusCodeRereading(string Phase, string Name, string AtStop, string AfterStop);

internal sealed record MySqlDataReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] ExcludedSources,
    PhaseSpanSet[] SpanSets,
    StatusCodeRereading[] StatusCodeRereadings,
    CapturedActivity[] Activities)
{
    internal const string SelectStatement = "SELECT 1 AS qyl_probe_value";
    internal const string FailingStatement = "SELECT qyl_missing_column FROM qyl_missing_table";

    internal const string PhaseStartup = "startup";
    internal const string PhasePhysicalOpen = "physical-open";
    internal const string PhaseSelect = "command-select";
    internal const string PhaseFailingSelect = "command-failing-select";
    internal const string PhaseClose = "close";
    internal const string PhasePooledOpen = "pooled-open";
    internal const string PhasePooledClose = "pooled-close";
    internal const string PhaseShutdown = "shutdown";

    // The one exclusion rule. "Experimental.*" is the runtime's own socket, DNS and TLS
    // diagnostics: qyl never subscribes those sources, and they appear here only because this
    // demo listens to every source in the process. No other prefix is excluded, so a span from
    // any third source lands in the asserted set and fails the gate.
    private const string ExcludedSourcePrefix = "Experimental.";

    private const string ConnectorNet = QylTelemetryNames.VendorActivitySources.ConnectorNet;
    private const string QylScope = "Qyl.Telemetry.AutoInstrumentation";
    private const string ConnectorNetClient = ConnectorNet + "/Client";

    private const string StatementSpanName = "SQL Statement";
    private const string PooledConnectionSpanName = "Connection (pooled)";

    // MySql.Data's own attribute vocabulary, measured against MySql.Data 26.7.0. These are the
    // driver's strings, several of them conventions the registry has since deprecated; qyl passes
    // them through untouched, so the demo names them as literals rather than pretending they are
    // current registry constants.
    private const string DbSystem = "db.system";
    private const string DbName = "db.name";
    private const string DbUser = "db.user";
    private const string DbStatement = "db.statement";
    private const string DbConnectionString = "db.connection_string";
    private const string DbResponseStatusCode = "db.response.status_code";
    private const string ErrorType = "error.type";
    private const string NetTransport = "net.transport";
    private const string NetPeerPort = "net.peer.port";
    private const string ThreadId = "thread.id";
    private const string ThreadName = "thread.name";
    private const string OtelStatusCode = "otel.status_code";
    private const string StatusOk = "OK";
    private const string StatusError = "ERROR";

    private static readonly string[] StatementTagKeys =
    [
        QylAttributes.InstrumentationDomain,
        DbSystem,
        DbName,
        DbUser,
        DbStatement,
        ThreadId,
        ThreadName,
        OtelStatusCode,
    ];

    private static readonly string[] ConnectionTagKeys =
    [
        QylAttributes.InstrumentationDomain,
        NetTransport,
        DbConnectionString,
        NetPeerPort,
        OtelStatusCode,
    ];

    // The exact span set per operation. A physical open traces the driver's five handshake
    // statements; the Connection activity it starts stops only at Close/Dispose, which is why it
    // lands in the close phase and not in the open one. A pooled re-open traces nothing at open
    // and only the pooled Connection span at close. Every user command is exactly one span.
    private static readonly PhaseExpectation[] Expectations =
    [
        new(PhasePhysicalOpen, [.. Enumerable.Repeat(ConnectorNetClient, 5)]),
        new(PhaseSelect, [ConnectorNetClient]),
        new(PhaseFailingSelect, [ConnectorNetClient]),
        new(PhaseClose, [ConnectorNetClient]),
        new(PhasePooledOpen, []),
        new(PhasePooledClose, [ConnectorNetClient]),
    ];

    public static MySqlDataReport Create(
        string runtimeMode,
        List<CapturedActivity> observedSpans,
        List<Activity> exportedActivities)
    {
        var failures = new List<string>();

        var asserted = observedSpans
            .Where(static span => !span.Source.StartsWith(ExcludedSourcePrefix, StringComparison.Ordinal))
            .ToArray();
        var excludedSources = observedSpans
            .Where(static span => span.Source.StartsWith(ExcludedSourcePrefix, StringComparison.Ordinal))
            .Select(static span => span.Source)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Every source the listener saw is either MySql.Data's or one the exclusion rule names.
        // A third source anywhere -- including a reappearing qyl interceptor span -- fails here.
        foreach (var source in asserted.Select(static span => span.Source).Distinct(StringComparer.Ordinal))
        {
            if (!StringComparer.Ordinal.Equals(source, ConnectorNet))
                failures.Add($"unexpected activity source {source}");
        }

        // The qyl DbCommand interceptor lane for MySql.Data is deleted. Its span reappearing is a
        // failure on its own, stated separately from the span-set rule that also catches it.
        var qylSpans = observedSpans.Count(static span => StringComparer.Ordinal.Equals(span.Source, QylScope));
        if (qylSpans is not 0)
            failures.Add($"expected 0 spans from {QylScope}, got {qylSpans.ToString(CultureInfo.InvariantCulture)}");

        var spanSets = new List<PhaseSpanSet>(Expectations.Length);
        foreach (var expectation in Expectations)
        {
            var actual = asserted
                .Where(span => StringComparer.Ordinal.Equals(span.Phase, expectation.Phase))
                .Select(static span => span.Tuple)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expected = expectation.Tuples.Order(StringComparer.Ordinal).ToArray();
            spanSets.Add(new PhaseSpanSet(expectation.Phase, expected, actual));

            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{expectation.Phase} span set mismatch: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
            }
        }

        // Nothing is traced outside the operations: no span before the first Open, and none after
        // the last Dispose.
        foreach (var strayPhase in new[] { PhaseStartup, PhaseShutdown })
        {
            var stray = asserted.Count(span => StringComparer.Ordinal.Equals(span.Phase, strayPhase));
            if (stray is not 0)
                failures.Add($"expected no spans in phase {strayPhase}, got {stray.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var span in asserted)
        {
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client on {span.Name}, got {span.Kind}");

            // The attribute the qyl processor owns, and the only one it writes. It is already on
            // the span when the listener snapshots it, because the SDK's processors run first.
            RequireTag(span, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.DbClient, failures);
        }

        VerifyStatementSpans(asserted, failures);
        VerifyConnectionSpans(asserted, failures);

        var rereadings = RereadStatusCodes(asserted, exportedActivities, failures);

        return new MySqlDataReport(
            runtimeMode,
            failures.Count is 0,
            [.. failures],
            excludedSources,
            [.. spanSets],
            rereadings,
            asserted);
    }

    /// <summary>
    /// Reads <c>otel.status_code</c> off the exported activities a second time, once the database
    /// work is over.
    /// </summary>
    /// <remarks>
    /// MySql.Data 26.7.0 keeps writing to an activity it has already stopped: the failing command's
    /// span carries <c>ERROR</c> at the instant a processor sees it and reads <c>OK</c> afterwards.
    /// A simple export processor therefore records the failure and a batching one records success
    /// from the same span, so the demo asserts both readings rather than picking one.
    /// </remarks>
    private static StatusCodeRereading[] RereadStatusCodes(
        CapturedActivity[] asserted,
        List<Activity> exportedActivities,
        ICollection<string> failures)
    {
        var afterStop = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var activity in exportedActivities)
        {
            if (!StringComparer.Ordinal.Equals(activity.Source.Name, ConnectorNet) || activity.Id is not { } id)
                continue;

            afterStop[id] = Convert.ToString(
                activity.GetTagItem(OtelStatusCode),
                CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // Every span the listener saw reached the exporter: qyl subscribes the driver's source and
        // drops nothing.
        if (afterStop.Count != asserted.Length)
        {
            failures.Add(
                $"exported {afterStop.Count.ToString(CultureInfo.InvariantCulture)} {ConnectorNet} spans but the listener saw {asserted.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        var rereadings = new List<StatusCodeRereading>(asserted.Length);
        foreach (var span in asserted)
        {
            var atStop = span.Tags.TryGetValue(OtelStatusCode, out var tag) ? tag.Value : string.Empty;
            var after = afterStop.GetValueOrDefault(span.Id, string.Empty);
            rereadings.Add(new StatusCodeRereading(span.Phase, span.Name, atStop, after));

            // Once stopped, every span reads OK -- the failing one included.
            if (!StringComparer.Ordinal.Equals(after, StatusOk))
                failures.Add($"expected {OtelStatusCode}={StatusOk} after stop on {span.Name} ({span.Phase}), got {after}");
        }

        return [.. rereadings];
    }

    private static void VerifyStatementSpans(CapturedActivity[] asserted, ICollection<string> failures)
    {
        var statements = asserted
            .Where(static span => StringComparer.Ordinal.Equals(span.Name, StatementSpanName))
            .ToArray();

        // Five handshake statements plus the two the demo issues. Every one of them carries the
        // same display name: MySql.Data never names a span after the query it ran.
        if (statements.Length != 7)
            failures.Add($"expected 7 '{StatementSpanName}' spans, got {statements.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in statements)
        {
            RequireExactTagKeys(span, StatementTagKeys, failures);
            RequireTag(span, DbSystem, "mysql", failures);
            RequireTagType(span, ThreadId, nameof(Int32), failures);
            RequireTagType(span, ThreadName, nameof(String), failures);

            // db.statement is unconditional: MySql.Data writes the full command text with no
            // opt-in, and OTEL_SEMCONV_STABILITY_OPT_IN does not change that.
            if (!span.Tags.TryGetValue(DbStatement, out var statement) || statement.Value.Length is 0)
                failures.Add($"missing {DbStatement} on {span.Name}");
        }

        var selects = statements.Where(static span => StringComparer.Ordinal.Equals(span.Phase, PhaseSelect)).ToArray();
        if (selects.Length != 1)
        {
            failures.Add($"expected 1 statement span in {PhaseSelect}, got {selects.Length.ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            RequireTag(selects[0], DbStatement, SelectStatement, failures);
            RequireTag(selects[0], OtelStatusCode, StatusOk, failures);
            RequireStatus(selects[0], nameof(ActivityStatusCode.Unset), failures);
            if (selects[0].Events.Length is not 0)
                failures.Add($"expected no events on the succeeding statement span, got {selects[0].Events.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        var failing = statements.Where(static span => StringComparer.Ordinal.Equals(span.Phase, PhaseFailingSelect)).ToArray();
        if (failing.Length != 1)
        {
            failures.Add($"expected 1 statement span in {PhaseFailingSelect}, got {failing.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        var failed = failing[0];
        RequireTag(failed, DbStatement, FailingStatement, failures);
        RequireTag(failed, OtelStatusCode, StatusError, failures);

        // The finding an exporter has to know about: MySql.Data reports the failure in the
        // otel.status_code tag and in an exception event, and never touches Activity.Status. An
        // exporter that reads Activity.Status sees this failed command as unset -- as OK.
        RequireStatus(failed, nameof(ActivityStatusCode.Unset), failures);
        if (failed.StatusDescription is not null)
            failures.Add($"expected null StatusDescription on the failing statement span, got {failed.StatusDescription}");

        // Nor does the driver write the error conventions: no error.type, no
        // db.response.status_code. RequireExactTagKeys already forbids them; naming them makes
        // the omission the assertion it is.
        RequireMissingTag(failed, ErrorType, failures);
        RequireMissingTag(failed, DbResponseStatusCode, failures);

        if (failed.Events.Length != 1)
        {
            failures.Add($"expected exactly 1 event on the failing statement span, got {failed.Events.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        var exceptionEvent = failed.Events[0];
        if (!StringComparer.Ordinal.Equals(exceptionEvent.Name, "exception"))
            failures.Add($"expected an 'exception' event, got {exceptionEvent.Name}");

        RequireEventTag(exceptionEvent, "exception.type", typeof(MySqlException).FullName!, failures);
        foreach (var key in new[] { "exception.message", "exception.stacktrace" })
        {
            if (!exceptionEvent.Tags.TryGetValue(key, out var value) || value.Length is 0)
                failures.Add($"missing {key} on the exception event");
        }
    }

    private static void VerifyConnectionSpans(CapturedActivity[] asserted, ICollection<string> failures)
    {
        var connections = asserted
            .Where(static span => StringComparer.Ordinal.Equals(span.Name, PooledConnectionSpanName))
            .ToArray();

        // One per connection the demo opens: the physical one and the pooled re-open. The name
        // carries "(pooled)" because pooling is on; it is "Connection" with Pooling=false.
        if (connections.Length != 2)
            failures.Add($"expected 2 '{PooledConnectionSpanName}' spans, got {connections.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in connections)
        {
            RequireExactTagKeys(span, ConnectionTagKeys, failures);
            RequireTag(span, OtelStatusCode, StatusOk, failures);
            RequireStatus(span, nameof(ActivityStatusCode.Unset), failures);
            if (span.Events.Length is not 0)
                failures.Add($"expected no events on {span.Name}, got {span.Events.Length.ToString(CultureInfo.InvariantCulture)}");

            // net.transport is a MySqlConnectionProtocol enum value, not the string the convention
            // asks for, and net.peer.port is unsigned. Both reach the collector as the driver
            // typed them.
            RequireTagType(span, NetTransport, nameof(MySqlConnectionProtocol), failures);
            RequireTagType(span, NetPeerPort, nameof(UInt32), failures);
        }
    }

    private static void RequireExactTagKeys(CapturedActivity span, string[] expected, ICollection<string> failures)
    {
        var actual = span.Tags.Keys.Order(StringComparer.Ordinal).ToArray();
        var wanted = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(wanted, StringComparer.Ordinal))
            failures.Add($"{span.Name} ({span.Phase}) tag keys: expected [{string.Join(", ", wanted)}], got [{string.Join(", ", actual)}]");
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on {span.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual.Value, expected))
            failures.Add($"expected {key}={expected} on {span.Name}, got {actual.Value}");
    }

    private static void RequireTagType(CapturedActivity span, string key, string expectedType, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on {span.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual.ValueType, expectedType))
            failures.Add($"expected {key} typed {expectedType} on {span.Name}, got {actual.ValueType}");
    }

    private static void RequireMissingTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (span.Tags.ContainsKey(key))
            failures.Add($"unexpected {key} on {span.Name}");
    }

    private static void RequireStatus(CapturedActivity span, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(span.Status, expected))
            failures.Add($"expected Activity.Status {expected} on {span.Name} ({span.Phase}), got {span.Status}");
    }

    private static void RequireEventTag(CapturedEvent capturedEvent, string key, string expected, ICollection<string> failures)
    {
        if (!capturedEvent.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on the {capturedEvent.Name} event");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private sealed record PhaseExpectation(string Phase, string[] Tuples);
}

[JsonSerializable(typeof(MySqlDataReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMySqlDataJsonContext : JsonSerializerContext;
