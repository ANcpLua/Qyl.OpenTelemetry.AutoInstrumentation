using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Qyl.Api.Contracts.Common;
using Qyl.Api.Contracts.Common.Pagination;
using Qyl.Api.Contracts.Health;
using Qyl.Api.Contracts.OTel.Logs;
using Qyl.Api.Contracts.OTel.Metrics;
using Qyl.Api.Contracts.OTel.Traces;

namespace Qyl.Build;

/// <summary>
/// The collector's query API, typed by Qyl.Api.Contracts: the exact request, response and model types
/// the collector serves. (Qyl.Api.Sdk is the MSBuild SDK for building an API, not a client of this one;
/// the contract package is the typed surface, and this class is its HTTP transport.)
/// </summary>
sealed class QylCollectorClient : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.General);
    private readonly HttpClient _http;

    public QylCollectorClient(Uri baseAddress)
    {
        _http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public HealthReport Health() => Get<HealthReport>("/health");

    /// <summary>Every span of one trace.</summary>
    public IReadOnlyList<Span> TraceSpans(string traceId) =>
        Pages<CursorPageSpan, Span>($"/api/v1/traces/{Uri.EscapeDataString(traceId)}/spans?limit=1000", static page => (page.Items, page.NextCursor));

    /// <summary>The traces with a span from <paramref name="service"/> that started in the window.</summary>
    public IReadOnlyList<Trace> Traces(string service, DateTimeOffset start, DateTimeOffset end) =>
        Pages<CursorPageTrace, Trace>(
            $"/api/v1/traces?service_name={Uri.EscapeDataString(service)}&start_time={Time(start)}&end_time={Time(end)}&limit=1000",
            static page => (page.Items, page.NextCursor));

    /// <summary>Every series of one instrument, across services.</summary>
    public IReadOnlyList<MetricSeries> MetricSeries(string metricName) =>
        Pages<CursorPageMetricSeries, MetricSeries>($"/api/v1/metrics/{Uri.EscapeDataString(metricName)}/series?limit=1000", static page => (page.Items, page.NextCursor));

    /// <summary>The log records <paramref name="service"/> emitted in the window.</summary>
    public IReadOnlyList<LogRecord> Logs(string service, DateTimeOffset start, DateTimeOffset end) =>
        Pages<CursorPageLogRecord, LogRecord>(
            $"/api/v1/logs?service_name={Uri.EscapeDataString(service)}&start_time={Time(start)}&end_time={Time(end)}&limit=1000",
            static page => (page.Items, page.NextCursor));

    public void Dispose() => _http.Dispose();

    private static string Time(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture));

    private IReadOnlyList<TItem> Pages<TPage, TItem>(string path, Func<TPage, (IReadOnlyList<TItem> Items, string? NextCursor)> read)
    {
        var items = new List<TItem>();
        var next = path;
        while (true)
        {
            var (page, cursor) = read(Get<TPage>(next));
            items.AddRange(page);
            if (cursor is null)
                return items;
            next = path + "&cursor=" + Uri.EscapeDataString(cursor);
        }
    }

    private T Get<T>(string path)
    {
        using var response = _http.GetAsync(path).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw Gate.Fail($"collector GET {path} answered {(int)response.StatusCode}: {body}");
        }

        return response.Content.ReadFromJsonAsync<T>(s_json).GetAwaiter().GetResult()
               ?? throw Gate.Fail($"collector GET {path} returned no body");
    }
}

/// <summary>Collector attribute values in the text form the demos' own reports use
/// (<c>Convert.ToString(value, InvariantCulture)</c>).</summary>
static class CollectorValues
{
    public static IReadOnlyDictionary<string, string?> Attributes(IReadOnlyList<Qyl.Api.Contracts.Common.Attribute>? attributes)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in attributes ?? [])
            result[attribute.Key] = Text(attribute.Value);
        return result;
    }

    public static string? Text(AttributeValue? value) => value switch
    {
        null => null,
        AttributeValue.StringValue text => text.Value,
        AttributeValue.BoolValue flag => flag.Value ? "True" : "False",
        AttributeValue.ObjectValue { Value: AttributeIntValue number } => number.Value.ToString(CultureInfo.InvariantCulture),
        AttributeValue.ObjectValue { Value: AttributeDoubleValue number } => number.Value.ToString(CultureInfo.InvariantCulture),
        // A multi-valued attribute has no single text the demos agree on (CLR type name, joined, JSON).
        AttributeValue.ArrayValue => null,
        _ => value.ToStableString(),
    };
}
