using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

internal static class QylHttpClientMetrics
{
    private static readonly Meter Meter = new("System.Net.Http");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("http.client.request.duration", "s");

    public static void RecordRequestDuration(DateTime startTimeUtc, string? method, int? statusCode)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
            return;

        var elapsed = TimeProvider.System.GetUtcNow().UtcDateTime - startTimeUtc;
        if (elapsed.TotalSeconds >= 0)
        {
            var tags = new TagList
            {
                { QylSemanticAttributes.HttpRequestMethod, NormalizeMethod(method) },
            };

            if (statusCode is { } code)
                tags.Add(QylSemanticAttributes.HttpResponseStatusCode, code);

            RequestDuration.Record(elapsed.TotalSeconds, in tags);
        }
    }

    private static string NormalizeMethod(string? method)
        => method switch
        {
            "CONNECT" or "DELETE" or "GET" or "HEAD" or "OPTIONS" or "PATCH" or "POST" or "PUT" or "TRACE" => method,
            _ => "_OTHER",
        };
}
