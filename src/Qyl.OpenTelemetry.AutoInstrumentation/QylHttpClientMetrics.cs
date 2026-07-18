using System.Diagnostics;
using System.Diagnostics.Metrics;

using Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

internal static class QylHttpClientMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.HttpClientMeterName);
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(QylMetricNames.HttpClientRequestDuration, "s");

    public static bool IsRecordingEnabled
        => IsRecordingEnabledFor(QylAutoInstrumentationOptions.Current);

    public static bool IsRecordingEnabledFor(QylAutoInstrumentationOptions options)
        => RequestDuration.Enabled &&
           options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient);

    public static void RecordRequestDuration(DateTime startTimeUtc, string? method, int? statusCode)
    {
        if (!IsRecordingEnabled)
            return;

        RecordRequestDurationUnchecked(startTimeUtc, method, statusCode);
    }

    internal static void RecordRequestDurationUnchecked(DateTime startTimeUtc, string? method, int? statusCode)
    {
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
