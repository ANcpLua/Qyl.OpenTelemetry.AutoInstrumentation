using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Trace;
using Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using ExceptionAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Exception.ExceptionAttributes;
using ExceptionEventDefinitions = Qyl.Telemetry.SemanticConventions.Events.ExceptionEventDefinitions;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

var connectionString = Environment.GetEnvironmentVariable("QYL_POSTGRES_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_POSTGRES_CONNECTION_STRING is required.");
    return 2;
}

var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;

// Readiness runs before the host is built, so no ActivitySource in the process has a listener yet
// and the retries emit nothing at all. It also runs unpooled, which leaves the pool the asserted
// run uses empty: the first Open below is a physical connect, and Npgsql traces only those.
await WaitForPostgresAsync(connectionString);

// The real registration path: AddQyl subscribes to Npgsql's own ActivitySource and installs the one
// native-span processor. 15.0.0 deleted the qyl DbCommand interceptor for this driver, so every
// span below is written by Npgsql itself and qyl adds exactly one attribute to it.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealNpgsqlDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-npgsql-demo";
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

await using var connection = new NpgsqlConnection(connectionString);

using (recorder.Measure(NpgsqlReport.ConnectOperation))
    await connection.OpenAsync();

Console.WriteLine("connection-state=" + connection.State.ToString());

using (recorder.Measure(NpgsqlReport.SelectOperation))
{
    await using var command = new NpgsqlCommand(NpgsqlReport.SelectText, connection);
    var scalar = await command.ExecuteScalarAsync();
    Console.WriteLine("select-scalar=" + (Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? string.Empty));
}

using (recorder.Measure(NpgsqlReport.FailingSelectOperation))
{
    try
    {
        await using var command = new NpgsqlCommand(NpgsqlReport.FailingSelectText, connection);
        _ = await command.ExecuteScalarAsync();
    }
    catch (PostgresException exception)
    {
        Console.WriteLine("expected-postgres-sqlstate=" + exception.SqlState);
    }
}

using (recorder.Measure(NpgsqlReport.CloseOperation))
    await connection.CloseAsync();

using (recorder.Measure(NpgsqlReport.PooledReopenOperation))
    await connection.OpenAsync();

// Stop listening before the flush. Exporting is itself an HTTP call, and this listener is
// unfiltered: the OTLP POST the demo's own exporter makes would otherwise land in the asserted span
// set as a System.Net.Http span. Every database operation is over by this point.
everySourceListener.Dispose();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = NpgsqlReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    database,
    recorder.SnapshotAll(),
    recorder.SnapshotWindows(),
    [.. exportedActivities.Select(CapturedActivity.From)]);

var json = JsonSerializer.Serialize(report, RealNpgsqlJsonContext.Default.NpgsqlReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForPostgresAsync(string connectionString)
{
    var unpooled = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    Exception? lastException = null;

    for (var attempt = 0; attempt < 120; attempt++)
    {
        try
        {
            await using var connection = new NpgsqlConnection(unpooled);
            await connection.OpenAsync();
            return;
        }
        catch (NpgsqlException exception)
        {
            lastException = exception;
        }
        catch (TimeoutException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("PostgreSQL did not become ready.", lastException);
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
    // report keeps only its head. The key set and exception.type are what the gate reads.
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

internal sealed record NpgsqlReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] ExcludedSourcePrefixes,
    string[] ExcludedSourceNames,
    OperationWindow[] Operations,
    CapturedActivity[] Activities)
{
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

    // 42703 is the PostgreSQL SQLSTATE for undefined_column. Npgsql writes the SQLSTATE -- not the
    // CLR exception type -- into error.type, db.response.status_code and the status description.
    private const string UndefinedColumnSqlState = "42703";

    // Npgsql's own event name for the first byte of the server's reply. The registry publishes no
    // name for it: it is a vendor event that reaches the collector unchanged.
    private const string ReceivedFirstResponseEvent = "received-first-response";

    private const string SourceName = QylTelemetryNames.VendorActivitySources.Npgsql;

    private static readonly string ExceptionEventName = ExceptionEventDefinitions.Exception.Name;
    private const string ClientSpan = SourceName + "|" + nameof(ActivityKind.Client);

    /// <summary>The exact tag-key set the CONNECT span carries.</summary>
    private static readonly string[] ConnectTagKeys =
    [
        DbAttributes.Namespace,
        DbAttributes.SystemName,
        DbIncubatingAttributes.NpgsqlConnectionId,
        DbIncubatingAttributes.NpgsqlDataSource,
        QylAttributes.InstrumentationDomain,
        ServerAttributes.Address,
        ServerAttributes.Port,
    ];

    /// <summary>
    /// The exact tag-key set a successful command span carries. db.query.text is in it
    /// unconditionally: Npgsql emits the stable flavour always, and
    /// <c>OTEL_SEMCONV_STABILITY_OPT_IN=database</c> changes nothing about that.
    /// </summary>
    private static readonly string[] CommandTagKeys = [.. ConnectTagKeys, DbAttributes.QueryText];

    /// <summary>
    /// The exact tag-key set a FAILED command span carries: the successful set plus the two keys
    /// that name the SQLSTATE.
    /// </summary>
    private static readonly string[] FailedCommandTagKeys =
        [.. CommandTagKeys, DbAttributes.ResponseStatusCode, ErrorAttributes.Type];

    /// <summary>
    /// The exact sorted multiset of <c>(ActivitySource.Name, Activity.Kind)</c> tuples each
    /// operation must produce -- not a filtered subset and not a lower bound.
    /// </summary>
    private static readonly KeyValuePair<string, string[]>[] ExpectedWindows =
    [
        // A physical connect is one CONNECT span.
        new(ConnectOperation, [ClientSpan]),
        new(SelectOperation, [ClientSpan]),
        new(FailingSelectOperation, [ClientSpan]),
        // Returning the connection to the pool is not traced, and neither is taking it back out:
        // only a physical connect produces a CONNECT span.
        new(CloseOperation, []),
        new(PooledReopenOperation, []),
    ];

    public static NpgsqlReport Create(
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
            VerifyConnect(connect[0], database, failures);
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
                $"{librarySpans.Length.ToString(CultureInfo.InvariantCulture)} Npgsql spans");
        }

        return new NpgsqlReport(
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

    private static void VerifyConnect(CapturedActivity span, string database, ICollection<string> failures)
    {
        // The connect span is named for the database it opened, not for a statement.
        RequireName(span, "CONNECT " + database, failures);
        RequireStatus(span, nameof(ActivityStatusCode.Unset), failures);
        RequireTag(span, DbAttributes.SystemName, DbAttributes.SystemNameValues.Postgresql, failures);
        RequireTag(span, DbAttributes.Namespace, database, failures);
        RequirePresentTag(span, DbIncubatingAttributes.NpgsqlDataSource, failures);
        RequirePresentTag(span, ServerAttributes.Address, failures);
        RequireTagType(span, ServerAttributes.Port, nameof(Int32), failures);
        RequireTagType(span, DbIncubatingAttributes.NpgsqlConnectionId, nameof(Int32), failures);
        // Opening a connection runs no statement, so there is nothing to record.
        RequireTagKeys(span, ConnectTagKeys, failures);
        RequireEvents(span, [], failures);
    }

    private static void VerifySucceededCommand(CapturedActivity span, string database, ICollection<string> failures)
    {
        // The command span is named for the database system alone: Npgsql puts no statement summary
        // in the name.
        RequireName(span, DbAttributes.SystemNameValues.Postgresql, failures);
        RequireStatus(span, nameof(ActivityStatusCode.Unset), failures);
        // Unconditional: Npgsql emits the stable flavour always, and
        // OTEL_SEMCONV_STABILITY_OPT_IN=database changes nothing about it.
        RequireTag(span, DbAttributes.QueryText, SelectText, failures);
        RequireTagKeys(span, CommandTagKeys, failures);
        VerifyCommonCommandTags(span, database, failures);
        // Success adds one untagged event and nothing else.
        RequireEvents(span, [ReceivedFirstResponseEvent], failures);
        if (span.Events.Length is 1 && span.Events[0].Tags.Count is not 0)
            failures.Add($"expected {ReceivedFirstResponseEvent} to carry no tags");
    }

    private static void VerifyFailedCommand(CapturedActivity span, string database, ICollection<string> failures)
    {
        RequireName(span, DbAttributes.SystemNameValues.Postgresql, failures);
        RequireStatus(span, nameof(ActivityStatusCode.Error), failures);
        if (!StringComparer.Ordinal.Equals(span.StatusDescription, UndefinedColumnSqlState))
            failures.Add($"expected status description {UndefinedColumnSqlState}, got {span.StatusDescription}");

        RequireTag(span, DbAttributes.QueryText, FailingSelectText, failures);
        RequireTagKeys(span, FailedCommandTagKeys, failures);
        VerifyCommonCommandTags(span, database, failures);
        RequireTag(span, DbAttributes.ResponseStatusCode, UndefinedColumnSqlState, failures);
        // error.type is the SQLSTATE, NOT the CLR exception type -- the exception type is on the
        // event instead.
        RequireTag(span, ErrorAttributes.Type, UndefinedColumnSqlState, failures);

        // The failure replaces the success event rather than adding to it: no first response ever
        // arrived.
        RequireEvents(span, [ExceptionEventName], failures);
        var exception = Array.Find(
            span.Events,
            static candidate => StringComparer.Ordinal.Equals(candidate.Name, ExceptionEventName));
        if (exception is null)
            return;

        RequireEventTag(exception, ExceptionAttributes.Type, typeof(PostgresException).FullName!, failures);
        RequireEventTagPresent(exception, ExceptionAttributes.Message, failures);
        RequireEventTagPresent(exception, ExceptionAttributes.Stacktrace, failures);
    }

    private static void VerifyCommonCommandTags(CapturedActivity span, string database, ICollection<string> failures)
    {
        RequireTag(span, DbAttributes.SystemName, DbAttributes.SystemNameValues.Postgresql, failures);
        RequireTag(span, DbAttributes.Namespace, database, failures);
        RequirePresentTag(span, ServerAttributes.Address, failures);
        RequireTagType(span, ServerAttributes.Port, nameof(Int32), failures);
        RequirePresentTag(span, DbIncubatingAttributes.NpgsqlDataSource, failures);
        RequireTagType(span, DbIncubatingAttributes.NpgsqlConnectionId, nameof(Int32), failures);
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

    private static void RequireEventTag(
        CapturedEvent activityEvent,
        string key,
        string expected,
        ICollection<string> failures)
    {
        if (!activityEvent.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key} on event {activityEvent.Name}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected} on event {activityEvent.Name}, got {actual}");
    }

    private static void RequireEventTagPresent(CapturedEvent activityEvent, string key, ICollection<string> failures)
    {
        if (!activityEvent.Tags.ContainsKey(key))
            failures.Add($"missing {key} on event {activityEvent.Name}");
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

[JsonSerializable(typeof(NpgsqlReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealNpgsqlJsonContext : JsonSerializerContext;
