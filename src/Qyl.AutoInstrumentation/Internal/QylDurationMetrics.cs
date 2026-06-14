namespace Qyl.AutoInstrumentation.Internal;

internal static class QylDurationMetrics
{
    public static DateTime GetHttpClientStartTimeUtc()
        => QylHttpClientMetrics.IsRecordingEnabled ? TimeProvider.System.GetUtcNow().UtcDateTime : default;

    public static DateTime GetHttpClientStartTimeUtc(bool metricsEnabled)
        => metricsEnabled ? TimeProvider.System.GetUtcNow().UtcDateTime : default;

    public static void RecordHttpClientDuration(DateTime startTimeUtc, string? method, int? statusCode)
        => QylHttpClientMetrics.RecordRequestDuration(startTimeUtc, method, statusCode);

    public static void RecordHttpClientDurationUnchecked(DateTime startTimeUtc, string? method, int? statusCode)
        => QylHttpClientMetrics.RecordRequestDurationUnchecked(startTimeUtc, method, statusCode);

    public static long GetDbClientStartTimestamp()
        => QylDbClientMetrics.GetTimestamp();

    public static bool IsDbClientRecordingEnabled(string instrumentationId)
        => QylDbClientMetrics.IsRecordingEnabled(instrumentationId);

    public static void RecordDbClientDuration(long startTimestamp, string instrumentationId)
        => QylDbClientMetrics.RecordDuration(startTimestamp, instrumentationId);

    public static long GetNServiceBusStartTimestamp()
        => QylNServiceBusMetrics.GetTimestamp();

    public static void RecordNServiceBusDuration(long startTimestamp, string operationName)
        => QylNServiceBusMetrics.RecordDuration(startTimestamp, operationName);
}
