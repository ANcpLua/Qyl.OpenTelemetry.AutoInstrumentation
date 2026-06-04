using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

internal static class QylHttpClientMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.HttpClientMeterName);
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(QylMetricNames.HttpClientRequestDuration, "s");

    public static void RecordRequestDuration(DateTime startTimeUtc, string? method, int? statusCode)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
            return;

        var elapsed = TimeProvider.System.GetUtcNow().UtcDateTime - startTimeUtc;
        if (elapsed.TotalSeconds >= 0)
        {
            var tags = new TagList
            {
                { QylSemanticAttributes.HttpRequestMethod, QylHttpMethod.Normalize(method) },
            };

            if (statusCode is { } code)
                tags.Add(QylSemanticAttributes.HttpResponseStatusCode, code);

            RequestDuration.Record(elapsed.TotalSeconds, in tags);
        }
    }

}
