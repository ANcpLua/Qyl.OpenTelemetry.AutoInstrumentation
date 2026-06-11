using System.Diagnostics;
using System.Net;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted HTTP Web Request.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedHttpWebRequest);</code></example>
public static class QylInterceptedHttpWebRequest
{

    /// <summary>Runs the Get Start Time Utc runtime helper used by source-generated qyl interceptors.</summary>
    public static DateTime GetStartTimeUtc()
        => QylHttpClientMetrics.IsRecordingEnabled ? TimeProvider.System.GetUtcNow().UtcDateTime : default;

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(HttpWebRequest request, string methodName)
    {
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient))
            return null;

        var method = QylHttpMethod.Normalize(request.Method);
        var activity = QylActivitySource.StartActivity(QylActivityNames.HttpClient(method), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpWebRequest);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);

        if (request.RequestUri is not null)
        {
            activity.SetTag(QylSemanticAttributes.ServerAddress, request.RequestUri.Host);

            if (!request.RequestUri.IsDefaultPort)
                activity.SetTag(QylSemanticAttributes.ServerPort, request.RequestUri.Port);

            if (options.CaptureSensitiveValues)
            {
                var requestUri = request.RequestUri.ToString();
                activity.SetTag(QylSemanticAttributes.UrlFull, QylCaptureHelpers.FormatUrlFull(
                    requestUri,
                    options.HttpClientUrlQueryRedactionDisabled));
            }
        }

        QylCaptureHelpers.SetRequestHeaders(activity, options.HttpClientCapturedRequestHeaderMap, request.Headers);
        return activity;
    }

    /// <summary>Runs the Record Result runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordResult(Activity? activity, DateTime startTimeUtc, string? method, object? result)
    {
        int? statusCode = null;
        var response = result as HttpWebResponse;
        if (response is not null)
            statusCode = RecordResponse(activity, response, markErrorForStatus: true);

        RecordDuration(startTimeUtc, method, statusCode);
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, DateTime startTimeUtc, string? method, Exception exception)
    {
        int? statusCode = null;
        if (exception is WebException { Response: HttpWebResponse response })
            statusCode = RecordResponse(activity, response, markErrorForStatus: false);

        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        RecordDuration(startTimeUtc, method, statusCode);
    }

    private static int RecordResponse(Activity? activity, HttpWebResponse response, bool markErrorForStatus)
    {
        var statusCode = (int)response.StatusCode;
        if (activity is not null)
        {
            activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, statusCode);
            QylCaptureHelpers.SetRequestHeaders(
                activity,
                QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaderMap,
                response.Headers);
            if (markErrorForStatus && statusCode >= 400)
            {
                activity.SetTag(QylSemanticAttributes.ErrorType, statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }

        return statusCode;
    }

    private static void RecordDuration(DateTime startTimeUtc, string? method, int? statusCode)
    {
        if (startTimeUtc == default)
            return;

        QylHttpClientMetrics.RecordRequestDuration(
            startTimeUtc,
            method,
            statusCode);
    }

}
