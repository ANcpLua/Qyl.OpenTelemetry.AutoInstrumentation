using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

internal static class QylHttpClientMetrics
{
    private static readonly Meter Meter = new("System.Net.Http");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("http.client.request.duration", "s");

    public static void RecordRequestDuration(DateTime startTimeUtc)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
            return;

        var elapsed = TimeProvider.System.GetUtcNow().UtcDateTime - startTimeUtc;
        if (elapsed.TotalSeconds >= 0)
            RequestDuration.Record(elapsed.TotalSeconds);
    }
}
