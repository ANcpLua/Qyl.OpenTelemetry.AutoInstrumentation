using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using OpenTelemetry.Trace;
using Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

// MySqlConnector picks its attribute flavour from this variable the first time it touches its own
// ActivitySource, and the README documents qyl against the stable one. Setting it here, before any
// MySqlConnector type is loaded, makes the demo self-contained; the verifier passes the same value
// in the environment so the two can never disagree.
Environment.SetEnvironmentVariable(
    MySqlConnectorReport.StabilityOptInVariable,
    MySqlConnectorReport.StabilityOptInDatabase);

var connectionString = Environment.GetEnvironmentVariable("QYL_MYSQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_MYSQL_CONNECTION_STRING is required.");
    return 2;
}

var database = new MySqlConnectionStringBuilder(connectionString).Database;

// Readiness runs before the host is built, so no ActivitySource in the process has a listener yet
// and the retries emit nothing at all. It also runs unpooled, which leaves the pool the asserted
// run uses empty.
await WaitForMySqlAsync(connectionString);

// The real registration path: AddQyl subscribes to MySqlConnector's own ActivitySource and installs
// the one native-span processor. 15.0.0 deleted the qyl DbCommand interceptor for this driver, so
// every span below is written by MySqlConnector itself and qyl adds exactly one attribute to it.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealMySqlConnectorDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-mysqlconnector-demo";
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

// In ADDITION to the in-memory exporter this gate listens to EVERY ActivitySource in the process,
// so a second instrumentation linked into the build cannot hide behind a source filter: its spans
// land in the same asserted multiset and fail it. The ONE exclusion is the `Experimental.` prefix --
// the runtime's own socket, DNS and TLS diagnostics, which qyl never subscribes to and which are
// visible here only because this listener is unfiltered. No other prefix is excluded.
var recorder = new SpanRecorder();
using var everySourceListener = new ActivityListener
{
    ShouldListenTo = static _ => true,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = recorder.Record,
};
ActivitySource.AddActivityListener(everySourceListener);

await using var connection = new MySqlConnection(connectionString);

using (recorder.Measure(MySqlConnectorReport.ConnectOperation))
    await connection.OpenAsync();

Console.WriteLine("connection-state=" + connection.State.ToString());

using (recorder.Measure(MySqlConnectorReport.SelectOperation))
{
    await using var command = new MySqlCommand(MySqlConnectorReport.SelectText, connection);
    var scalar = await command.ExecuteScalarAsync();
    Console.WriteLine("select-scalar=" + (Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? string.Empty));
}

using (recorder.Measure(MySqlConnectorReport.FailingSelectOperation))
{
    try
    {
        await using var command = new MySqlCommand(MySqlConnectorReport.FailingSelectText, connection);
        _ = await command.ExecuteScalarAsync();
    }
    catch (MySqlException exception)
    {
        // The error number the span must carry in error.type and db.response.status_code -- but
        // not as an exception event: see MySqlConnectorReport.VerifyFailedCommand.
        Console.WriteLine(
            "expected-mysql-error-number=" +
            ((int)exception.ErrorCode).ToString(CultureInfo.InvariantCulture));
    }
}

using (recorder.Measure(MySqlConnectorReport.CloseOperation))
    await connection.CloseAsync();

using (recorder.Measure(MySqlConnectorReport.PooledReopenOperation))
    await connection.OpenAsync();

// Stop listening before the flush. Exporting is itself an HTTP call, and this listener is
// unfiltered: the OTLP POST the demo's own exporter makes would otherwise land in the asserted span
// set as a System.Net.Http span. Every database operation is over by this point.
everySourceListener.Dispose();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = MySqlConnectorReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    database,
    recorder.SnapshotAll(),
    recorder.SnapshotWindows(),
    [.. exportedActivities.Select(CapturedActivity.From)]);

var json = JsonSerializer.Serialize(report, RealMySqlConnectorJsonContext.Default.MySqlConnectorReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForMySqlAsync(string connectionString)
{
    var unpooled = new MySqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    Exception? lastException = null;

    for (var attempt = 0; attempt < 180; attempt++)
    {
        try
        {
            await using var connection = new MySqlConnection(unpooled);
            await connection.OpenAsync();
            return;
        }
        catch (MySqlException exception)
        {
            lastException = exception;
        }
        catch (TimeoutException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("MySQL did not become ready.", lastException);
}

/// <summary>
/// Records every stopped <see cref="Activity"/> in the process and, while a window is open, the
/// subset a single database operation produced. Windows are what the gate asserts on: one exact
/// multiset of (source, kind) tuples per operation.
/// </summary>
internal sealed class SpanRecorder
{
    private readonly Lock _gate = new();
    private readonly List<Activity> _all = [];
    private readonly List<KeyValuePair<string, List<Activity>>> _windows = [];
    private List<Activity>? _current;

    internal void Record(Activity activity)
    {
        // Async drivers stop their activities on a pool thread, so the two lists are guarded.
        lock (_gate)
        {
            _all.Add(activity);
            _current?.Add(activity);
        }
    }

    internal IDisposable Measure(string operation)
    {
        var window = new List<Activity>();
        lock (_gate)
        {
            _windows.Add(new KeyValuePair<string, List<Activity>>(operation, window));
            _current = window;
        }

        return new Window(this);
    }

    internal CapturedActivity[] SnapshotAll()
    {
        lock (_gate)
            return [.. _all.Select(CapturedActivity.From)];
    }

    internal RecordedWindow[] SnapshotWindows()
    {
        lock (_gate)
            return
            [
                .. _windows.Select(static window =>
                    new RecordedWindow(window.Key, [.. window.Value.Select(CapturedActivity.From)])),
            ];
    }

    private void Close()
    {
        lock (_gate)
            _current = null;
    }

    private sealed class Window(SpanRecorder recorder) : IDisposable
    {
        public void Dispose() => recorder.Close();
    }
}

internal sealed record RecordedWindow(string Operation, CapturedActivity[] Spans);

internal sealed record CapturedEvent(string Name, IReadOnlyDictionary<string, string> Tags)
{
    // A stack trace is the bulk of an exception event and nothing is asserted on its text, so the
    // report keeps only its head.
    private const int MaximumValueLength = 160;

    public static CapturedEvent From(ActivityEvent activityEvent)
        => new(
            activityEvent.Name,
            activityEvent.Tags.ToDictionary(
                static tag => tag.Key,
                static tag => Truncate(Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty),
                StringComparer.Ordinal));

    private static string Truncate(string value)
        => value.Length <= MaximumValueLength ? value : value[..MaximumValueLength] + "...";
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    string StatusDescription,
    string SpanId,
    string ParentSpanId,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyDictionary<string, string> TagTypes,
    CapturedEvent[] Events)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.StatusDescription ?? string.Empty,
            activity.SpanId.ToHexString(),
            activity.ParentSpanId.ToHexString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => tag.Value?.GetType().Name ?? "null",
                StringComparer.Ordinal),
            [.. activity.Events.Select(CapturedEvent.From)]);
}

internal sealed record OperationWindow(
    string Operation,
    string[] Expected,
    string[] Actual,
    CapturedActivity[] Spans);

internal sealed record MySqlConnectorReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] ExcludedSourcePrefixes,
    string[] ExcludedSourceNames,
    OperationWindow[] Operations,
    CapturedActivity[] Activities)
{
    internal const string StabilityOptInVariable = "OTEL_SEMCONV_STABILITY_OPT_IN";
    internal const string StabilityOptInDatabase = "database";

    internal const string ConnectOperation = "connection-open";
    internal const string SelectOperation = "select";
    internal const string FailingSelectOperation = "failing-select";
    internal const string CloseOperation = "connection-close";
    internal const string PooledReopenOperation = "pooled-reopen";

    internal const string SelectText = "SELECT 1";
    internal const string FailingSelectText = "SELECT missing_column";

    // The runtime's own socket / DNS / TLS diagnostics. They are in this process only because the
    // listener above is unfiltered; qyl never subscribes to them. This is the ONLY excluded prefix.
    private const string ExcludedSourcePrefix = "Experimental.";

    // MySqlConnector's own span names.
    private const string OpenSpanName = "Open";
    private const string ExecuteSpanName = "Execute";

    // MySqlConnector's own connection-id key. The registry publishes no constant for it -- unlike
    // db.npgsql.connection_id it was never registered upstream -- so it reaches the collector as an
    // unchanged pass-through tag.
    private const string ConnectionIdTag = "db.connection_id";

    // A root activity's parent, which is what every span here carries: the demo opens no ambient
    // activity, and Execute is NOT nested under the Open that preceded it.
    private const string NoParentSpanId = "0000000000000000";

    // ER_BAD_FIELD_ERROR, which `SELECT missing_column` raises. MySqlConnector writes the number
    // into the attributes and the MySqlErrorCode name into the status description.
    private const string BadFieldErrorName = nameof(MySqlErrorCode.BadFieldError);

    private const string SourceName = QylTelemetryNames.VendorActivitySources.MySqlConnector;
    private const string ClientSpan = SourceName + "|" + nameof(ActivityKind.Client);

    private static readonly string BadFieldErrorNumber =
        ((int)MySqlErrorCode.BadFieldError).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The exact sorted multiset of <c>(ActivitySource.Name, Activity.Kind)</c> tuples each
    /// operation must produce -- not a filtered subset and not a lower bound.
    /// </summary>
    private static readonly KeyValuePair<string, string[]>[] ExpectedWindows =
    [
        new(ConnectOperation, [ClientSpan]),
        new(SelectOperation, [ClientSpan]),
        new(FailingSelectOperation, [ClientSpan]),
        // Returning the connection to the pool is not traced...
        new(CloseOperation, []),
        // ...but taking it back out is: MySqlConnector traces EVERY Open, pooled or not, which is
        // where it differs from Npgsql.
        new(PooledReopenOperation, [ClientSpan]),
    ];

    /// <summary>
    /// The exact tag-key set an Open span carries: MySqlConnector describes the whole connection on
    /// it, and qyl adds its one attribute.
    /// </summary>
    private static readonly string[] OpenTagKeys =
    [
        ConnectionIdTag,
        DbAttributes.Namespace,
        DbAttributes.SystemName,
        NetworkAttributes.PeerAddress,
        NetworkAttributes.PeerPort,
        QylAttributes.InstrumentationDomain,
        ServerAttributes.Address,
        ServerAttributes.Port,
    ];

    /// <summary>
    /// The exact tag-key set a successful Execute span carries under
    /// <c>OTEL_SEMCONV_STABILITY_OPT_IN=database</c>, asserted as a set rather than as a list of
    /// absences: the experimental flavour's <c>db.system</c>, <c>db.name</c>, <c>db.statement</c>,
    /// <c>db.connection_string</c>, <c>db.user</c>, <c>net.transport</c>, <c>net.peer.ip</c>,
    /// <c>net.peer.port</c> and <c>thread.id</c> are gone because they are not in this set.
    /// </summary>
    private static readonly string[] ExecuteTagKeys = [.. OpenTagKeys, DbAttributes.QueryText];

    /// <summary>
    /// The exact tag-key set a FAILED Execute span carries: the successful set plus the two keys
    /// that name the MySQL error number.
    /// </summary>
    private static readonly string[] FailedExecuteTagKeys =
        [.. ExecuteTagKeys, DbAttributes.ResponseStatusCode, ErrorAttributes.Type];

    public static MySqlConnectorReport Create(
        string runtimeMode,
        string database,
        CapturedActivity[] allSpans,
        RecordedWindow[] windows,
        CapturedActivity[] exportedSpans)
    {
        var failures = new List<string>();
        var observed = allSpans.Where(IsAsserted).ToArray();
        var excludedNames = allSpans
            .Where(static span => !IsAsserted(span))
            .Select(static span => span.Source)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Every span that is not a runtime `Experimental.` diagnostic must come from the driver's
        // own source. An OpenTelemetry.Instrumentation.* package loaded alongside fails here.
        foreach (var source in observed
                     .Select(static span => span.Source)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            if (!StringComparer.Ordinal.Equals(source, SourceName))
                failures.Add($"unexpected activity source {source}");
        }

        // Called out on its own because it is the regression 15.0.0 must never undo: the qyl
        // DbCommand interceptor is deleted, so its scope may not produce a single span.
        var interceptorSpans = allSpans.Count(static span => StringComparer.Ordinal.Equals(
            span.Source,
            QylTelemetryNames.Scopes.QylTelemetryAutoInstrumentation));
        if (interceptorSpans is not 0)
        {
            failures.Add(
                $"{QylTelemetryNames.Scopes.QylTelemetryAutoInstrumentation} produced " +
                $"{interceptorSpans.ToString(CultureInfo.InvariantCulture)} spans; the interceptor is deleted");
        }

        var operations = new List<OperationWindow>(ExpectedWindows.Length);
        foreach (var expectation in ExpectedWindows)
        {
            var window = Array.Find(
                windows,
                candidate => StringComparer.Ordinal.Equals(candidate.Operation, expectation.Key));
            if (window is null)
            {
                failures.Add($"missing operation window {expectation.Key}");
                continue;
            }

            var spans = window.Spans.Where(IsAsserted).ToArray();
            var actual = spans.Select(Tuple).Order(StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(expectation.Value, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{expectation.Key}: expected [{string.Join(", ", expectation.Value)}], " +
                    $"got [{string.Join(", ", actual)}]");
            }

            operations.Add(new OperationWindow(expectation.Key, expectation.Value, actual, spans));
        }

        var byOperation = operations.ToDictionary(
            static operation => operation.Operation,
            static operation => operation.Spans,
            StringComparer.Ordinal);

        if (byOperation.TryGetValue(ConnectOperation, out var connect) && connect.Length is 1)
            VerifyOpen(connect[0], database, failures);
        if (byOperation.TryGetValue(PooledReopenOperation, out var reopen) && reopen.Length is 1)
            VerifyOpen(reopen[0], database, failures);
        if (byOperation.TryGetValue(SelectOperation, out var select) && select.Length is 1)
            VerifySucceededCommand(select[0], database, failures);
        if (byOperation.TryGetValue(FailingSelectOperation, out var failing) && failing.Length is 1)
            VerifyFailedCommand(failing[0], database, failures);

        var librarySpans = observed
            .Where(static span => StringComparer.Ordinal.Equals(span.Source, SourceName))
            .ToArray();
        foreach (var span in librarySpans)
        {
            // The one attribute qyl owns on a native span. Without it the collector cannot classify
            // the span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.DbClient,
                failures);

            // ...and it is the only one. A second qyl.* key would mean the instrumentation had
            // started writing over what the library emitted.
            foreach (var key in span.Tags.Keys)
            {
                if (key.StartsWith("qyl.", StringComparison.Ordinal) &&
                    !StringComparer.Ordinal.Equals(key, QylAttributes.InstrumentationDomain))
                    failures.Add($"unexpected qyl attribute {key} on span {span.Name}");
            }

            if (!StringComparer.Ordinal.Equals(span.Kind, nameof(ActivityKind.Client)))
                failures.Add($"expected kind Client on span {span.Name}, got {span.Kind}");
        }

        // The windows account for every span the process produced: nothing was emitted outside them.
        var expectedTotal = ExpectedWindows.Sum(static expectation => expectation.Value.Length);
        if (observed.Length != expectedTotal)
        {
            failures.Add(
                $"expected {expectedTotal.ToString(CultureInfo.InvariantCulture)} spans outside the " +
                $"{ExcludedSourcePrefix} prefix, got {observed.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        // AddQyl must export every one of them: subscribing to the source is the whole delivery path
        // now that the interceptor is gone.
        var exported = exportedSpans
            .Where(static span => StringComparer.Ordinal.Equals(span.Source, SourceName))
            .ToArray();
        if (exported.Length != librarySpans.Length)
        {
            failures.Add(
                $"AddQyl exported {exported.Length.ToString(CultureInfo.InvariantCulture)} of the " +
                $"{librarySpans.Length.ToString(CultureInfo.InvariantCulture)} MySqlConnector spans");
        }

        return new MySqlConnectorReport(
            runtimeMode,
            failures.Count is 0,
            [.. failures],
            [ExcludedSourcePrefix],
            excludedNames,
            [.. operations],
            observed);
    }

    private static bool IsAsserted(CapturedActivity span)
        => !span.Source.StartsWith(ExcludedSourcePrefix, StringComparison.Ordinal);

    private static string Tuple(CapturedActivity span) => span.Source + "|" + span.Kind;

    private static void VerifyOpen(CapturedActivity span, string database, ICollection<string> failures)
    {
        RequireName(span, OpenSpanName, failures);
        RequireStatus(span, nameof(ActivityStatusCode.Unset), failures);
        RequireStatusDescription(span, string.Empty, failures);
        RequireEvents(span, [], failures);
        // Opening a connection runs no statement, so the Open span carries the connection tags and
        // nothing else.
        RequireTagKeys(span, OpenTagKeys, failures);
        VerifyConnectionTags(span, database, failures);
    }

    private static void VerifySucceededCommand(CapturedActivity span, string database, ICollection<string> failures)
    {
        RequireName(span, ExecuteSpanName, failures);
        RequireStatus(span, nameof(ActivityStatusCode.Unset), failures);
        RequireStatusDescription(span, string.Empty, failures);
        RequireEvents(span, [], failures);
        RequireTag(span, DbAttributes.QueryText, SelectText, failures);
        RequireTagKeys(span, ExecuteTagKeys, failures);
        VerifyConnectionTags(span, database, failures);
    }

    private static void VerifyFailedCommand(CapturedActivity span, string database, ICollection<string> failures)
    {
        RequireName(span, ExecuteSpanName, failures);
        RequireTag(span, DbAttributes.QueryText, FailingSelectText, failures);
        RequireTagKeys(span, FailedExecuteTagKeys, failures);
        VerifyConnectionTags(span, database, failures);

        // MySqlConnector 2.6.2 DOES mark the failure: Error status, the MySqlErrorCode name as the
        // description, and the MySQL error number in both error.type and db.response.status_code.
        RequireStatus(span, nameof(ActivityStatusCode.Error), failures);
        RequireStatusDescription(span, BadFieldErrorName, failures);
        RequireTag(span, ErrorAttributes.Type, BadFieldErrorNumber, failures);
        RequireTag(span, DbAttributes.ResponseStatusCode, BadFieldErrorNumber, failures);

        // What it does NOT do is record an exception: there is no `exception` event, so the CLR
        // exception type, its message and its stack trace never leave the process. That is the whole
        // difference from Npgsql, which attaches one.
        RequireEvents(span, [], failures);
    }

    private static void VerifyConnectionTags(CapturedActivity span, string database, ICollection<string> failures)
    {
        // Every span here is parented on the ambient activity, and this demo opens none -- so an
        // Execute span is a root span. It is NOT nested under the Open span that preceded it.
        if (!StringComparer.Ordinal.Equals(span.ParentSpanId, NoParentSpanId))
            failures.Add($"expected span {span.Name} to be a root span, got parent {span.ParentSpanId}");

        RequireTag(span, DbAttributes.SystemName, DbAttributes.SystemNameValues.Mysql, failures);
        RequireTag(span, DbAttributes.Namespace, database, failures);
        RequirePresentTag(span, ServerAttributes.Address, failures);
        RequireTagType(span, ServerAttributes.Port, nameof(Int32), failures);
        RequirePresentTag(span, NetworkAttributes.PeerAddress, failures);
        RequireTagType(span, NetworkAttributes.PeerPort, nameof(Int32), failures);
        // A String, not an Int32 -- MySqlConnector formats the connection id itself.
        RequireTagType(span, ConnectionIdTag, nameof(String), failures);
    }

    private static void RequireName(CapturedActivity span, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(span.Name, expected))
            failures.Add($"expected span name {expected}, got {span.Name}");
    }

    private static void RequireStatus(CapturedActivity span, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(span.Status, expected))
            failures.Add($"expected span {span.Name} status {expected}, got {span.Status}");
    }

    private static void RequireStatusDescription(CapturedActivity span, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(span.StatusDescription, expected))
        {
            failures.Add(
                $"expected span {span.Name} status description '{expected}', got '{span.StatusDescription}'");
        }
    }

    private static void RequireEvents(CapturedActivity span, string[] expected, ICollection<string> failures)
    {
        var actual = span.Events.Select(static activityEvent => activityEvent.Name).Order(StringComparer.Ordinal);
        if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            failures.Add(
                $"span {span.Name}: expected events [{string.Join(", ", expected)}], " +
                $"got [{string.Join(", ", span.Events.Select(static activityEvent => activityEvent.Name))}]");
        }
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on span {span.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected} on span {span.Name}, got {actual}");
    }

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key} on span {span.Name}");
    }

    private static void RequireTagType(
        CapturedActivity span,
        string key,
        string expectedType,
        ICollection<string> failures)
    {
        if (!span.TagTypes.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on span {span.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expectedType))
            failures.Add($"expected {key} to be {expectedType} on span {span.Name}, got {actual}");
    }

    private static void RequireMissingTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (span.Tags.ContainsKey(key))
            failures.Add($"unexpected {key} on span {span.Name}");
    }

    private static void RequireTagKeys(CapturedActivity span, string[] expected, ICollection<string> failures)
    {
        var actual = span.Tags.Keys.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            failures.Add(
                $"span {span.Name}: expected tag keys [{string.Join(", ", expected.Order(StringComparer.Ordinal))}], " +
                $"got [{string.Join(", ", actual)}]");
        }
    }
}

[JsonSerializable(typeof(MySqlConnectorReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMySqlConnectorJsonContext : JsonSerializerContext;
