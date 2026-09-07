using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.OpenTelemetry;
using Qyl;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

var connectionString = Environment.GetEnvironmentVariable("QYL_ORACLE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_ORACLE_CONNECTION_STRING is required.");
    return 2;
}

var mode = Environment.GetEnvironmentVariable("QYL_ORACLE_ADDON_MODE") ?? OracleMdaReport.ModeNone;
if (!OracleMdaReport.Modes.Contains(mode, StringComparer.Ordinal))
{
    Console.Error.WriteLine($"QYL_ORACLE_ADDON_MODE must be one of {string.Join(", ", OracleMdaReport.Modes)}.");
    return 2;
}

// Pooling off makes every Open a physical connect, so the connection spans the add-on can be told
// to emit -- Open around Connect, Close around DisConnect -- are the same in every run instead of
// depending on what the readiness probe left in the pool.
var unpooledConnectionString =
    new OracleConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
await WaitForOracleAsync(unpooledConnectionString);

// The real registration path: AddQyl subscribes to ODP.NET's own ActivitySource
// ("Oracle.ManagedDataAccess.Core") and installs the one native-span processor.
// OracleConfiguration.OpenTelemetryTracing defaults to true, so that subscription is the whole
// requirement -- the spans below arrive with no vendor add-on registered at all.
Console.WriteLine("oracle-mode=" + mode);
Console.WriteLine("oracle-opentelemetry-tracing=" + OracleConfiguration.OpenTelemetryTracing.ToString(CultureInfo.InvariantCulture));

var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealOracleMdaDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-oraclemda-demo";
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
    .WithTracing(tracing =>
    {
        tracing.AddInMemoryExporter(exportedActivities);

        // Oracle.ManagedDataAccess.OpenTelemetry is enrichment, never a gate on the spans. Its
        // parameterless registration is measured here too, because on its own it changes nothing:
        // every option that adds an attribute is off by default.
        if (StringComparer.Ordinal.Equals(mode, OracleMdaReport.ModeAddonDefault))
            tracing.AddOracleDataProviderInstrumentation();

        if (StringComparer.Ordinal.Equals(mode, OracleMdaReport.ModeAddonEnriched))
        {
            tracing.AddOracleDataProviderInstrumentation(instrumentation =>
            {
                instrumentation.EnableConnectionLevelAttributes = true;
                instrumentation.SetDbStatementForText = true;
                instrumentation.EnableSqlIdTracing = true;
                instrumentation.RecordException = true;
                instrumentation.AddDBInfoToDisplayName = true;
                instrumentation.EnableOpenCloseTracing = EnableOpenCloseTracing.AllOpenClose;
            });
        }
    });

using var host = builder.Build();
await host.StartAsync();

// The second listener is the point of this demo. It listens to EVERY ActivitySource in the
// process and snapshots each activity as it stops, so the report can assert the exact span set a
// database operation produces rather than a filtered subset.
var observedSpans = new List<CapturedActivity>();
var phase = OracleMdaReport.PhaseStartup;
using var activityListener = new ActivityListener
{
    ShouldListenTo = static _ => true,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => observedSpans.Add(CapturedActivity.From(activity, phase)),
};
ActivitySource.AddActivityListener(activityListener);

var connection = new OracleConnection(unpooledConnectionString);

phase = OracleMdaReport.PhaseOpen;
connection.Open();
Console.WriteLine("oracle-open=1");

phase = OracleMdaReport.PhaseScalar;
await using (var command = new OracleCommand(OracleMdaReport.ScalarStatement, connection))
{
    var scalar = command.ExecuteScalar();
    Console.WriteLine("scalar-value=" + Convert.ToString(scalar, CultureInfo.InvariantCulture));
}

phase = OracleMdaReport.PhaseNonQuery;
await using (var command = new OracleCommand(OracleMdaReport.NonQueryStatement, connection))
{
    var affected = command.ExecuteNonQuery();
    Console.WriteLine("nonquery-rows=" + affected.ToString(CultureInfo.InvariantCulture));
}

phase = OracleMdaReport.PhaseFailing;
await using (var command = new OracleCommand(OracleMdaReport.FailingStatement, connection))
{
    try
    {
        _ = command.ExecuteScalar();
        Console.WriteLine("unexpected-oraclemda-success=1");
    }
    catch (OracleException exception)
    {
        Console.WriteLine("expected-oraclemda-error=" + exception.GetType().FullName);
    }
}

phase = OracleMdaReport.PhaseClose;
connection.Close();

// The listener stops here, before the flush below. What the OTLP exporter does with the spans is
// the demo's own telemetry, not a database operation.
activityListener.Dispose();
phase = OracleMdaReport.PhaseShutdown;
connection.Dispose();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = OracleMdaReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    mode,
    observedSpans,
    exportedActivities);

var json = JsonSerializer.Serialize(report, RealOracleMdaJsonContext.Default.OracleMdaReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForOracleAsync(string connectionString)
{
    Exception? lastException = null;

    // A cold gvenzl/oracle-free container can take minutes to reach "DATABASE IS READY TO USE".
    for (var attempt = 0; attempt < 450; attempt++)
    {
        try
        {
            await using var probe = new OracleConnection(connectionString);
            await probe.OpenAsync();
            await using var command = new OracleCommand("SELECT 1 FROM DUAL", probe);
            _ = await command.ExecuteScalarAsync();
            return;
        }
        catch (OracleException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    throw new InvalidOperationException("Oracle did not become ready.", lastException);
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
    string SpanId,
    string ParentSpanId,
    IReadOnlyDictionary<string, CapturedTag> Tags,
    CapturedEvent[] Events)
{
    public string Tuple => Source + "/" + Kind;

    public bool IsRoot => StringComparer.Ordinal.Equals(ParentSpanId, "0000000000000000");

    public static CapturedActivity From(Activity activity, string phase)
        => new(
            phase,
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.StatusDescription,
            activity.SpanId.ToHexString(),
            activity.ParentSpanId.ToHexString(),
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
                    StringComparer.Ordinal)))]);
}

/// <summary>The exact sorted multiset of (source, kind) tuples one database operation must produce.</summary>
internal sealed record PhaseSpanSet(string Phase, string[] Expected, string[] Actual);

internal sealed record OracleMdaReport(
    string RuntimeMode,
    string AddonMode,
    bool Pass,
    string[] Failures,
    string[] ExcludedSources,
    string[] TagVocabulary,
    PhaseSpanSet[] SpanSets,
    CapturedActivity[] Activities)
{
    /// <summary>No vendor add-on: <c>AddQyl()</c>'s AddSource is the whole registration.</summary>
    internal const string ModeNone = "none";

    /// <summary>The add-on registered with no options configured, which changes nothing.</summary>
    internal const string ModeAddonDefault = "addon-default";

    /// <summary>The add-on registered with its enrichment options turned on.</summary>
    internal const string ModeAddonEnriched = "addon-enriched";

    internal static readonly string[] Modes = [ModeNone, ModeAddonDefault, ModeAddonEnriched];

    internal const string ScalarStatement = "SELECT 1 FROM DUAL";
    internal const string NonQueryStatement = "SELECT 2 FROM DUAL";
    internal const string FailingStatement = "SELECT 3 FROM qyl_missing_table";

    /// <summary>The statement text the add-on reports: ODP.NET replaces every literal with a bind marker.</summary>
    private const string RedactedScalarStatement = "SELECT ? FROM DUAL";

    internal const string PhaseStartup = "startup";
    internal const string PhaseOpen = "open";
    internal const string PhaseScalar = "command-scalar";
    internal const string PhaseNonQuery = "command-nonquery";
    internal const string PhaseFailing = "command-failing";
    internal const string PhaseClose = "close";
    internal const string PhaseShutdown = "shutdown";

    // The one exclusion rule. "Experimental.*" is the runtime's own socket, DNS and TLS
    // diagnostics: qyl never subscribes those sources, and they appear here only because this
    // demo listens to every source in the process. No other prefix is excluded, so a span from
    // any third source lands in the asserted set and fails the gate.
    private const string ExcludedSourcePrefix = "Experimental.";

    private const string OracleSource = QylTelemetryNames.VendorActivitySources.OracleManagedDataAccessCore;
    private const string QylScope = "Qyl.Telemetry.AutoInstrumentation";
    private const string OracleClient = OracleSource + "/Client";

    private const string ScalarVerb = "ExecuteScalar";
    private const string NonQueryVerb = "ExecuteNonQuery";
    private const string ChildVerb = "SendExecuteRequest";
    private const string OpenVerb = "Open";
    private const string ConnectVerb = "Connect";
    private const string CloseVerb = "Close";
    private const string DisconnectVerb = "DisConnect";

    // ODP.NET's own attribute vocabulary, measured against Oracle.ManagedDataAccess.Core
    // 23.26.300. db.system is the key the driver writes, a convention the registry has since
    // deprecated; qyl passes it through untouched.
    private const string DbSystem = "db.system";
    private const string DbName = "db.name";
    private const string DbUser = "db.user";
    private const string DbStatement = "db.statement";
    private const string DbResponseReturnedRows = "db.response.returned_rows";
    private const string DbOdpRoundtripDuration = "db.odp.roundtrip.duration";
    private const string DbOdpRoundtripCount = "db.odp.roundtrip.count";
    private const string DbOdpConnectionId = "db.odp.connection.id";
    private const string DbOdpSqlId = "db.odp.sql_id";
    private const string ServerAddress = "server.address";
    private const string ServerPort = "server.port";
    private const string ErrorType = "error.type";

    /// <summary>Every key ODP.NET writes without the add-on: four, and the qyl domain the processor stamps.</summary>
    private static readonly string[] DefaultVocabulary =
    [
        QylAttributes.InstrumentationDomain,
        DbSystem,
        DbOdpRoundtripCount,
        DbOdpRoundtripDuration,
        DbResponseReturnedRows,
    ];

    /// <summary>The keys the add-on's enrichment options add on top of the default vocabulary.</summary>
    private static readonly string[] EnrichedOnlyKeys =
    [
        DbName,
        DbOdpConnectionId,
        DbOdpSqlId,
        DbStatement,
        ServerAddress,
        ServerPort,
        DbUser,
    ];

    public static OracleMdaReport Create(
        string runtimeMode,
        string addonMode,
        List<CapturedActivity> observedSpans,
        List<Activity> exportedActivities)
    {
        var failures = new List<string>();
        var enriched = StringComparer.Ordinal.Equals(addonMode, ModeAddonEnriched);

        var asserted = observedSpans
            .Where(static span => !span.Source.StartsWith(ExcludedSourcePrefix, StringComparison.Ordinal))
            .ToArray();
        var excludedSources = observedSpans
            .Where(static span => span.Source.StartsWith(ExcludedSourcePrefix, StringComparison.Ordinal))
            .Select(static span => span.Source)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Every source the listener saw is either ODP.NET's or one the exclusion rule names. A
        // third source anywhere -- including a reappearing qyl interceptor span -- fails here.
        foreach (var source in asserted.Select(static span => span.Source).Distinct(StringComparer.Ordinal))
        {
            if (!StringComparer.Ordinal.Equals(source, OracleSource))
                failures.Add($"unexpected activity source {source}");
        }

        // The qyl DbCommand interceptor lane for ODP.NET is deleted. Its span reappearing is a
        // failure on its own, stated separately from the span-set rule that also catches it.
        var qylSpans = observedSpans.Count(static span => StringComparer.Ordinal.Equals(span.Source, QylScope));
        if (qylSpans is not 0)
            failures.Add($"expected 0 spans from {QylScope}, got {qylSpans.ToString(CultureInfo.InvariantCulture)}");

        var spanSets = VerifySpanSets(asserted, enriched, failures);

        foreach (var span in asserted)
        {
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client on {span.Name}, got {span.Kind}");

            // The attribute the qyl processor owns, and the only one it writes.
            RequireTag(span, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.DbClient, failures);

            // ODP.NET never writes the error conventions, in either mode.
            RequireMissingTag(span, ErrorType, failures);
        }

        var vocabulary = asserted
            .SelectMany(static span => span.Tags.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (enriched)
            VerifyEnriched(asserted, vocabulary, failures);
        else
            VerifyDefault(asserted, vocabulary, failures);

        VerifyFailingCommand(asserted, enriched, failures);
        VerifyExport(asserted, exportedActivities, failures);

        return new OracleMdaReport(
            runtimeMode,
            addonMode,
            failures.Count is 0,
            [.. failures],
            excludedSources,
            vocabulary,
            spanSets,
            asserted);
    }

    /// <summary>
    /// The exact span set per operation, and the parent/child shape inside a command.
    /// </summary>
    /// <remarks>
    /// Open and Close trace nothing by default -- that is the finding, not an omission. With the
    /// add-on's open/close tracing on they become two spans each, an <c>Open</c> around a
    /// <c>Connect</c> and a <c>Close</c> around a <c>DisConnect</c>. A command is two spans in
    /// every mode: the verb the application called, and the <c>SendExecuteRequest</c> beneath it.
    /// </remarks>
    private static PhaseSpanSet[] VerifySpanSets(
        CapturedActivity[] asserted,
        bool enriched,
        ICollection<string> failures)
    {
        string[] connectionPhaseTuples = enriched ? [OracleClient, OracleClient] : [];
        PhaseExpectation[] expectations =
        [
            new(PhaseOpen, connectionPhaseTuples),
            new(PhaseScalar, [OracleClient, OracleClient]),
            new(PhaseNonQuery, [OracleClient, OracleClient]),
            new(PhaseFailing, [OracleClient, OracleClient]),
            new(PhaseClose, connectionPhaseTuples),
        ];

        var spanSets = new List<PhaseSpanSet>(expectations.Length);
        foreach (var expectation in expectations)
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

        // Nothing is traced outside the operations.
        foreach (var strayPhase in new[] { PhaseStartup, PhaseShutdown })
        {
            var stray = asserted.Count(span => StringComparer.Ordinal.Equals(span.Phase, strayPhase));
            if (stray is not 0)
                failures.Add($"expected no spans in phase {strayPhase}, got {stray.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var (phase, verb) in new[]
                 {
                     (PhaseScalar, ScalarVerb),
                     (PhaseNonQuery, NonQueryVerb),
                     (PhaseFailing, ScalarVerb),
                 })
        {
            RequireParentChild(asserted, phase, verb, ChildVerb, enriched, failures);
        }

        if (enriched)
        {
            RequireParentChild(asserted, PhaseOpen, OpenVerb, ConnectVerb, enriched: true, failures);
            RequireParentChild(asserted, PhaseClose, CloseVerb, DisconnectVerb, enriched: true, failures);
        }

        return [.. spanSets];
    }

    /// <summary>Asserts the operation is one root span with exactly one child beneath it.</summary>
    private static void RequireParentChild(
        CapturedActivity[] asserted,
        string phase,
        string rootVerb,
        string childVerb,
        bool enriched,
        ICollection<string> failures)
    {
        var spans = asserted.Where(span => StringComparer.Ordinal.Equals(span.Phase, phase)).ToArray();
        var roots = spans.Where(static span => span.IsRoot).ToArray();
        var children = spans.Where(static span => !span.IsRoot).ToArray();

        if (roots.Length != 1 || children.Length != 1)
        {
            failures.Add(
                $"{phase} expected 1 root and 1 child, got {roots.Length.ToString(CultureInfo.InvariantCulture)} and {children.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(children[0].ParentSpanId, roots[0].SpanId))
            failures.Add($"{phase} child {children[0].Name} is not a child of {roots[0].Name}");

        // The span is named after the method the application called. With the add-on's
        // AddDBInfoToDisplayName on, the name gains a " HOST:PORT:DATABASE" suffix -- which the
        // enriched lane reconstructs from the span's own server/db attributes.
        RequireDisplayName(roots[0], rootVerb, enriched, failures);
        RequireDisplayName(children[0], childVerb, enriched, failures);
    }

    private static void RequireDisplayName(
        CapturedActivity span,
        string verb,
        bool enriched,
        ICollection<string> failures)
    {
        if (!enriched)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, verb))
                failures.Add($"expected display name {verb}, got {span.Name}");
            return;
        }

        var address = span.Tags.GetValueOrDefault(ServerAddress)?.Value;
        var port = span.Tags.GetValueOrDefault(ServerPort)?.Value;
        var database = span.Tags.GetValueOrDefault(DbName)?.Value;
        if (address is null || port is null || database is null)
        {
            failures.Add($"{span.Name} is missing the server/database attributes its display name is built from");
            return;
        }

        var expected = $"{verb} {address}:{port}:{database}";
        if (!StringComparer.Ordinal.Equals(span.Name, expected))
            failures.Add($"expected display name {expected}, got {span.Name}");
    }

    /// <summary>
    /// The default vocabulary: four keys, and which span carries which.
    /// </summary>
    private static void VerifyDefault(
        CapturedActivity[] asserted,
        string[] vocabulary,
        ICollection<string> failures)
    {
        var expectedVocabulary = DefaultVocabulary.Order(StringComparer.Ordinal).ToArray();
        if (!vocabulary.SequenceEqual(expectedVocabulary, StringComparer.Ordinal))
        {
            failures.Add(
                $"default tag vocabulary: expected [{string.Join(", ", expectedVocabulary)}], got [{string.Join(", ", vocabulary)}]");
        }

        foreach (var span in asserted)
        {
            RequireTag(span, DbSystem, "oracle", failures);

            if (span.IsRoot)
            {
                // The command verb's own span: how long the roundtrips took and how many there were.
                RequireExactTagKeys(
                    span,
                    [QylAttributes.InstrumentationDomain, DbSystem, DbOdpRoundtripDuration, DbOdpRoundtripCount],
                    failures);
                RequireTagType(span, DbOdpRoundtripDuration, nameof(TimeSpan), failures);
                RequireTagType(span, DbOdpRoundtripCount, nameof(Int32), failures);
                continue;
            }

            // The request span beneath it. ExecuteNonQuery returns no rows, so its child omits
            // db.response.returned_rows entirely rather than reporting zero.
            string[] expectedKeys = StringComparer.Ordinal.Equals(span.Phase, PhaseNonQuery)
                ? [QylAttributes.InstrumentationDomain, DbSystem]
                : [QylAttributes.InstrumentationDomain, DbSystem, DbResponseReturnedRows];
            RequireExactTagKeys(span, expectedKeys, failures);
            if (!StringComparer.Ordinal.Equals(span.Phase, PhaseNonQuery))
                RequireTagType(span, DbResponseReturnedRows, nameof(Int32), failures);
        }
    }

    /// <summary>
    /// The attribute delta the add-on's enrichment options add: seven keys on top of the four
    /// ODP.NET writes on its own, and the connection spans that carry them without any command.
    /// </summary>
    private static void VerifyEnriched(
        CapturedActivity[] asserted,
        string[] vocabulary,
        ICollection<string> failures)
    {
        var expectedVocabulary = DefaultVocabulary
            .Concat(EnrichedOnlyKeys)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!vocabulary.SequenceEqual(expectedVocabulary, StringComparer.Ordinal))
        {
            failures.Add(
                $"enriched tag vocabulary: expected [{string.Join(", ", expectedVocabulary)}], got [{string.Join(", ", vocabulary)}]");
        }

        // Every span, command and connection alike, now names the server and the account.
        foreach (var span in asserted)
        {
            RequireTag(span, DbSystem, "oracle", failures);
            foreach (var key in new[] { DbName, DbUser, ServerAddress, ServerPort, DbOdpConnectionId })
                RequirePresentTag(span, key, failures);

            RequireTagType(span, ServerPort, nameof(Int32), failures);
        }

        // The connection spans exist only because the add-on's open/close tracing was turned on.
        // They name no statement: the Connect and DisConnect children carry the connection
        // attributes alone, and the Open and Close spans above them add the roundtrips the
        // connect itself cost.
        string[] connectionKeys =
        [
            QylAttributes.InstrumentationDomain, DbSystem, DbName, DbUser, ServerAddress, ServerPort,
            DbOdpConnectionId,
        ];
        foreach (var span in asserted.Where(static span =>
                     StringComparer.Ordinal.Equals(span.Phase, PhaseOpen) ||
                     StringComparer.Ordinal.Equals(span.Phase, PhaseClose)))
        {
            RequireExactTagKeys(
                span,
                span.IsRoot ? [.. connectionKeys, DbOdpRoundtripDuration, DbOdpRoundtripCount] : connectionKeys,
                failures);
        }

        var scalarSpans = asserted.Where(static span => StringComparer.Ordinal.Equals(span.Phase, PhaseScalar)).ToArray();
        foreach (var span in scalarSpans)
        {
            // The statement text the add-on reports is the parameterized form: ODP.NET replaces
            // every literal with a bind marker before it leaves the process.
            RequireTag(span, DbStatement, RedactedScalarStatement, failures);

            // The server's SQL_ID for that statement, which is what ties a span to a plan.
            RequirePresentTag(span, DbOdpSqlId, failures);
        }

        var scalarRoot = scalarSpans.FirstOrDefault(static span => span.IsRoot);
        if (scalarRoot is null)
        {
            failures.Add("enriched run has no root span for the scalar command");
            return;
        }

        // The four keys ODP.NET writes on its own are still there; the add-on adds, it never
        // replaces.
        RequireExactTagKeys(
            scalarRoot,
            [
                QylAttributes.InstrumentationDomain, DbSystem, DbName, DbUser, ServerAddress, ServerPort,
                DbOdpConnectionId, DbStatement, DbOdpSqlId, DbOdpRoundtripDuration, DbOdpRoundtripCount,
            ],
            failures);
        RequireTagType(scalarRoot, DbOdpRoundtripDuration, nameof(TimeSpan), failures);
    }

    /// <summary>
    /// The failing command. The root span carries the failure and the child stays Ok in both
    /// modes; what the add-on changes is whether the failure is described.
    /// </summary>
    private static void VerifyFailingCommand(
        CapturedActivity[] asserted,
        bool enriched,
        ICollection<string> failures)
    {
        var spans = asserted.Where(static span => StringComparer.Ordinal.Equals(span.Phase, PhaseFailing)).ToArray();
        var root = spans.FirstOrDefault(static span => span.IsRoot);
        var child = spans.FirstOrDefault(static span => !span.IsRoot);
        if (root is null || child is null)
        {
            failures.Add("the failing command did not produce a root and a child span");
            return;
        }

        RequireStatus(root, nameof(ActivityStatusCode.Error), failures);

        // The request reached the server and came back; it is the command that failed.
        RequireStatus(child, nameof(ActivityStatusCode.Ok), failures);

        if (!enriched)
        {
            // Without the add-on the failure is a status code and nothing more: no description,
            // no exception event, no error.type.
            if (root.StatusDescription is not null)
                failures.Add($"expected null StatusDescription on the failing root, got {root.StatusDescription}");
            if (root.Events.Length is not 0)
                failures.Add($"expected no events on the failing root, got {root.Events.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        // RecordException is one of the enrichment options: with it the ORA error becomes both the
        // status description and an exception event.
        if (string.IsNullOrEmpty(root.StatusDescription))
            failures.Add("expected a StatusDescription on the failing root in the enriched run");
        if (root.Events.Length != 1)
        {
            failures.Add($"expected exactly 1 event on the failing root, got {root.Events.Length.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(root.Events[0].Name, "exception"))
            failures.Add($"expected an 'exception' event, got {root.Events[0].Name}");
    }

    private static void VerifyExport(
        CapturedActivity[] asserted,
        List<Activity> exportedActivities,
        ICollection<string> failures)
    {
        // Every span the listener saw reached the exporter: qyl subscribes the driver's source and
        // drops nothing.
        var exported = exportedActivities.Count(static activity =>
            StringComparer.Ordinal.Equals(activity.Source.Name, OracleSource));
        if (exported != asserted.Length)
        {
            failures.Add(
                $"exported {exported.ToString(CultureInfo.InvariantCulture)} {OracleSource} spans but the listener saw {asserted.Length.ToString(CultureInfo.InvariantCulture)}");
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

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key} on {span.Name} ({span.Phase})");
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

    private sealed record PhaseExpectation(string Phase, string[] Tuples);
}

[JsonSerializable(typeof(OracleMdaReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealOracleMdaJsonContext : JsonSerializerContext;
