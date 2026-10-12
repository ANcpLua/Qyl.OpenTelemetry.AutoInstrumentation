using System.Text.Json;
using Qyl.Api.Contracts.OTel.Enums;
using Qyl.Api.Contracts.OTel.Traces;
using Qyl.Telemetry.SemanticConventions.Incubating.Mapping;

namespace Qyl.Build;

/// <summary>
/// What a lane asserts about the telemetry a run exported. Against the local collector, through its
/// typed API; in the live check the weaver listener receives the telemetry instead and judges it
/// against the registry, so there the checks have nothing to read.
/// </summary>
interface ITelemetryCheck
{
    /// <summary>Every span the report lists (by TraceId/SpanId) arrived with the reported name, kind,
    /// status and attribute values — for each attribute the collector keeps.</summary>
    void Spans(DemoRun run, JsonElement activities, string what);

    /// <summary>The collector holds exactly these span ids from the lane's service in the run's window.</summary>
    void ServiceSpansExactly(DemoRun run, IReadOnlyCollection<string> spanIds, string what);

    /// <summary>Each reported instrument has a series from the lane's service written during the run whose
    /// attributes carry the reported values (for each attribute the collector keeps).</summary>
    void Metrics(DemoRun run, IEnumerable<ReportedMetric> metrics);

    /// <summary>No series of the named instruments from the lane's service was written during the run.</summary>
    void NoMetrics(DemoRun run, IEnumerable<string> metricNames);

    /// <summary>The lane's service emitted exactly these log records during the run, in this order.</summary>
    void Logs(DemoRun run, IReadOnlyList<(string Severity, string Body)> expected);
}

sealed class CollectorTelemetryCheck(QylCollectorClient client, string serviceName) : ITelemetryCheck
{
    private static readonly TimeSpan s_arrival = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_settle = TimeSpan.FromSeconds(3);

    public string ServiceName { get; } = serviceName;

    public void Spans(DemoRun run, JsonElement activities, string what)
    {
        var expected = activities.EnumerateArray().ToArray();
        if (expected.Length == 0)
            return;

        var byTrace = expected
            .Select(activity => (TraceId: Id(run, activity, "TraceId"), SpanId: Id(run, activity, "SpanId"), Activity: activity))
            .GroupBy(static entry => entry.TraceId, StringComparer.Ordinal);
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var trace in byTrace)
        {
            var wanted = trace.Select(static entry => entry.SpanId).ToHashSet(StringComparer.Ordinal);
            var spans = Poll(() => client.TraceSpans(trace.Key), found => wanted.All(id => found.Any(span => span.SpanId == id)));
            foreach (var (_, spanId, activity) in trace)
            {
                var span = spans.FirstOrDefault(candidate => candidate.SpanId == spanId)
                           ?? throw Gate.Fail($"{run.Label}: {what} span {trace.Key}/{spanId} ({ReportedActivity.Text(activity, "Name")}) never reached the collector");
                Compare(run, what, activity, span, dropped);
            }
        }

        Log.Information("  {Label}: {Count} {What} span(s) arrived over OTLP as reported{Dropped}", run.Label, expected.Length, what,
            dropped.Count == 0 ? string.Empty : $" (collector policy keeps none of: {string.Join(", ", dropped)})");
    }

    public void ServiceSpansExactly(DemoRun run, IReadOnlyCollection<string> spanIds, string what)
    {
        var wanted = spanIds.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<Span> Read() => client.Traces(ServiceName, run.Started, run.Finished)
            .SelectMany(static trace => trace.Spans)
            .Where(span => span.Resource.ServiceName == ServiceName && Within(run, span))
            .ToArray();

        var spans = Poll(Read, found => wanted.All(id => found.Any(span => span.SpanId == id)));
        Thread.Sleep(s_settle);
        spans = Read();
        var actual = spans.Select(static span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        Gate.Require(actual.SetEquals(wanted), () =>
            $"{run.Label}: the collector holds {actual.Count} {what} span(s) from {ServiceName} for this run, the report names {wanted.Count}: " +
            $"missing={string.Join(",", wanted.Except(actual))} extra={string.Join(",", spans.Where(span => !wanted.Contains(span.SpanId)).Select(static span => $"{span.SpanId}:{span.Name}"))}");
        Log.Information("  {Label}: the collector holds exactly the {Count} {What} span(s) the report names", run.Label, wanted.Count, what);
    }

    public void Metrics(DemoRun run, IEnumerable<ReportedMetric> metrics)
    {
        var since = Since(run);
        var expected = metrics.ToArray();
        foreach (var metric in expected)
        {
            Poll(() => client.MetricSeries(metric.Name)
                    .Where(series => series.ServiceName == ServiceName && series.LastSeen >= since && Carries(series, metric.Attributes))
                    .ToArray(),
                found => found.Length > 0,
                $"{run.Label}: no {metric.Name} series from {ServiceName} with {string.Join(", ", metric.Attributes.Select(static pair => $"{pair.Key}={pair.Value}"))} reached the collector during the run");
        }

        var names = expected.Select(static metric => metric.Name).Distinct(StringComparer.Ordinal).ToArray();
        Log.Information("  {Label}: {Count} reported measurement series arrived over OTLP ({Names})", run.Label, expected.Length, string.Join(", ", names));
    }

    private static bool Carries(Qyl.Api.Contracts.OTel.Metrics.MetricSeries series, IReadOnlyDictionary<string, string> attributes)
    {
        var stored = CollectorValues.Attributes(series.Attributes);
        return attributes.All(pair => !stored.TryGetValue(pair.Key, out var value) || value is null || value == pair.Value);
    }

    public void NoMetrics(DemoRun run, IEnumerable<string> metricNames)
    {
        Thread.Sleep(s_settle);
        var since = Since(run).AddSeconds(1); // the run before this one may have flushed within the same second
        foreach (var name in metricNames.Distinct(StringComparer.Ordinal))
        {
            var series = client.MetricSeries(name).Where(candidate => candidate.ServiceName == ServiceName && candidate.LastSeen >= since).ToArray();
            Gate.Require(series.Length == 0, () => $"{run.Label}: {series.Length} {name} series from {ServiceName} reached the collector although the run disabled it");
        }

        Log.Information("  {Label}: no {Names} series reached the collector", run.Label, string.Join(", ", metricNames));
    }

    public void Logs(DemoRun run, IReadOnlyList<(string Severity, string Body)> expected)
    {
        IReadOnlyList<(ulong Time, string Severity, string Body)> Read() => client.Logs(ServiceName, run.Started, run.Finished)
            .Select(static record => (record.TimeUnixNano, Severity(record.SeverityNumber), CollectorValues.Text(record.Body) ?? string.Empty))
            .ToArray();

        var records = Poll(Read, found => found.Count >= expected.Count);
        Thread.Sleep(s_settle);
        records = Read();
        string Held() => string.Join("; ", records.Select(static record => (record.Severity, record.Body)));

        // The same records, in the reported order wherever their timestamps tell an order apart: two
        // records logged within one clock tick share a timestamp, and no order can be read from them.
        var unmatched = expected.Select(static (record, index) => (Record: record, Index: index)).ToList();
        var placed = new List<(ulong Time, int Index)>();
        Gate.Require(records.Count == expected.Count, () => $"{run.Label}: the collector holds these log records from {ServiceName}: [{Held()}], expected [{string.Join("; ", expected)}]");
        foreach (var (time, severity, body) in records)
        {
            var match = unmatched.FindIndex(candidate => candidate.Record == (severity, body));
            Gate.Require(match >= 0, () => $"{run.Label}: the collector holds these log records from {ServiceName}: [{Held()}], expected [{string.Join("; ", expected)}]");
            placed.Add((time, unmatched[match].Index));
            unmatched.RemoveAt(match);
        }

        var order = placed.OrderBy(static entry => entry.Time).ThenBy(static entry => entry.Index).Select(static entry => entry.Index).ToArray();
        Gate.Require(order.SequenceEqual(Enumerable.Range(0, expected.Count)),
            () => $"{run.Label}: the log records from {ServiceName} arrived out of the reported order: {string.Join(", ", order)}");
        Log.Information("  {Label}: {Count} log record(s) arrived over OTLP as reported", run.Label, expected.Count);
    }

    /// <summary>Series timestamps have second precision: the run's start, floored to its second.</summary>
    private static DateTimeOffset Since(DemoRun run) =>
        new(run.Started.UtcDateTime.Ticks - run.Started.UtcDateTime.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    /// <summary>The .NET LogLevel name the demos report for an OTLP severity.</summary>
    private static string Severity(SeverityNumber number) => number switch
    {
        >= SeverityNumber.Fatal => "Critical",
        >= SeverityNumber.Error => "Error",
        >= SeverityNumber.Warn => "Warning",
        >= SeverityNumber.Info => "Information",
        >= SeverityNumber.Debug => "Debug",
        >= SeverityNumber.Trace => "Trace",
        _ => "None",
    };

    private static bool Within(DemoRun run, Span span)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds((long)(span.StartTimeUnixNano / 1_000_000));
        return start >= run.Started && start <= run.Finished;
    }

    private static string Id(DemoRun run, JsonElement activity, string field) =>
        ReportedActivity.Text(activity, field) ?? throw Gate.Fail($"{run.Label}: a reported span carries no {field}: {activity}");

    private static void Compare(DemoRun run, string what, JsonElement activity, Span span, ISet<string> dropped)
    {
        var context = $"{run.Label}: {what} span {span.SpanId}";
        if (ReportedActivity.Text(activity, "Name") is { } name)
            Gate.Require(span.Name == name, $"{context} arrived named '{span.Name}', reported '{name}'");
        if (ReportedActivity.Text(activity, "Kind") is { } kind)
            Gate.Require(span.Kind.ToString() == kind, $"{context} arrived with kind {span.Kind}, reported {kind}");
        if (ReportedActivity.Text(activity, "Status") is { } status)
        {
            // An Unset Activity.Status with an otel.status_code tag is exported with the tag's status:
            // the OTLP exporter's documented fallback, which MySql.Data relies on.
            var expected = status == "Unset" && ReportedActivity.Tag(activity, "otel.status_code") is { } tagged
                ? tagged switch { "OK" => "Ok", "ERROR" => "Error", _ => status }
                : status;
            Gate.Require(span.Status.Code.ToString() == expected,
                $"{context} arrived with status {span.Status.Code}, reported {status}{(expected == status ? string.Empty : $" with otel.status_code={ReportedActivity.Tag(activity, "otel.status_code")}")}");
        }

        var attributes = CollectorValues.Attributes(span.Attributes);
        foreach (var key in ReportedActivity.TagKeys(activity))
        {
            var reported = ReportedActivity.Tag(activity, key);
            var stored = attributes.ContainsKey(key) ? key
                : AttributeMapping.TryGetRename(key, out var renamed) && attributes.ContainsKey(renamed) ? renamed
                : null;
            if (stored is null)
            {
                dropped.Add(key);
                continue;
            }

            // A multi-valued attribute (null text) and a CLR type name are not comparable text.
            if (reported is null || attributes[stored] is null || reported.StartsWith("System.", StringComparison.Ordinal) && reported.EndsWith("[]", StringComparison.Ordinal))
                continue;
            Gate.Require(attributes[stored] == reported, $"{context} arrived with {stored}={attributes[stored]}, reported {key}={reported}");
        }
    }

    private static T Poll<T>(Func<T> read, Func<T, bool> done, string? failure = null)
    {
        var deadline = DateTime.UtcNow + s_arrival;
        while (true)
        {
            var value = read();
            if (done(value))
                return value;
            if (DateTime.UtcNow >= deadline)
            {
                if (failure is not null)
                    throw Gate.Fail(failure);
                return value;
            }

            Thread.Sleep(500);
        }
    }
}

/// <summary>The live check: weaver receives the telemetry and judges it; there is nothing to read back.</summary>
sealed class WeaverJudgesTelemetry : ITelemetryCheck
{
    public void Spans(DemoRun run, JsonElement activities, string what) { }

    public void ServiceSpansExactly(DemoRun run, IReadOnlyCollection<string> spanIds, string what) { }

    public void Metrics(DemoRun run, IEnumerable<ReportedMetric> metrics) { }

    public void NoMetrics(DemoRun run, IEnumerable<string> metricNames) { }

    public void Logs(DemoRun run, IReadOnlyList<(string Severity, string Body)> expected) { }
}

/// <summary>One measurement series a demo reports: its instrument name and the attribute values it carried.</summary>
sealed record ReportedMetric(string Name, IReadOnlyDictionary<string, string> Attributes)
{
    public static ReportedMetric Named(string name) => new(name, new Dictionary<string, string>());
}
