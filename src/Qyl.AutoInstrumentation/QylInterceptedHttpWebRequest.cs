using System.Diagnostics;
using System.Net;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedHttpWebRequest
{

    public static DateTime GetStartTimeUtc()
        => TimeProvider.System.GetUtcNow().UtcDateTime;

    public static Activity? StartActivity(HttpWebRequest request, string methodName)
    {
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient))
            return null;

        var method = QylHttpMethod.Normalize(request.Method);
        var activity = QylActivitySource.Source.StartActivity("HTTP " + method, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpWebRequest);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);

        if (request.RequestUri is not null)
        {
            activity.SetTag(QylSemanticAttributes.ServerAddress, request.RequestUri.Host);

            if (!request.RequestUri.IsDefaultPort)
                activity.SetTag(QylSemanticAttributes.ServerPort, request.RequestUri.Port);

            if (options.CaptureSensitiveValues || options.HttpClientUrlQueryRedactionDisabled)
            {
                var requestUri = request.RequestUri.ToString();
                var urlFull = options.HttpClientUrlQueryRedactionDisabled
                    ? requestUri
                    : RedactQuery(requestUri);

                activity.SetTag(QylSemanticAttributes.UrlFull, urlFull);
            }
        }

        SetConfiguredHeaders(activity, QylSemanticAttributes.HttpRequestHeaderPrefix, options.HttpClientCapturedRequestHeaders, request.Headers);
        return activity;
    }

    public static void RecordResult(Activity? activity, DateTime startTimeUtc, string? method, object? result)
    {
        int? statusCode = null;
        var response = result as HttpWebResponse;
        if (response is not null)
            statusCode = RecordResponse(activity, response, markErrorForStatus: true);

        RecordDuration(startTimeUtc, method, statusCode);
    }

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
            SetConfiguredHeaders(activity, QylSemanticAttributes.HttpResponseHeaderPrefix, QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaders, response.Headers);
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
        QylHttpClientMetrics.RecordRequestDuration(
            startTimeUtc,
            method,
            statusCode);
    }

    private static void SetConfiguredHeaders(Activity activity, string prefix, string[] configuredHeaders, WebHeaderCollection headers)
    {
        if (configuredHeaders.Length is 0)
            return;

        foreach (var headerName in configuredHeaders)
        {
            var values = headers.GetValues(headerName);
            if (values is { Length: > 0 })
                activity.SetTag(prefix + NormalizeHeaderName(headerName), values);
        }
    }

    private static string NormalizeHeaderName(string headerName)
        => headerName.Trim().ToLowerInvariant().Replace('_', '-');

    private static string RedactQuery(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return url;

        var fragmentStart = url.IndexOf('#', queryStart);
        return fragmentStart < 0
            ? url[..queryStart] + "?Redacted"
            : url[..queryStart] + "?Redacted" + url[fragmentStart..];
    }
}
