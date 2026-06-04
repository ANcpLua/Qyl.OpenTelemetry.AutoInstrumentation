using System.Diagnostics;
using System.Net;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedHttpWebRequest
{
    private const string HttpWebRequestDomain = "http.webrequest";

    public static Activity? StartActivity(HttpWebRequest request, string methodName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient))
            return null;

        var activity = QylActivitySource.Source.StartActivity("HTTP " + request.Method, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, HttpWebRequestDomain);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, request.Method);

        if (request.RequestUri is not null)
        {
            var urlFull = QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled
                ? request.RequestUri.ToString()
                : request.RequestUri.GetLeftPart(UriPartial.Path);

            activity.SetTag(QylSemanticAttributes.UrlFull, urlFull);
            activity.SetTag(QylSemanticAttributes.ServerAddress, request.RequestUri.Host);

            if (!request.RequestUri.IsDefaultPort)
                activity.SetTag(QylSemanticAttributes.ServerPort, request.RequestUri.Port);
        }

        return activity;
    }

    public static void RecordResult(Activity? activity, object? result)
    {
        if (activity is not null && result is HttpWebResponse response)
            activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, (int)response.StatusCode);

        RecordDuration(activity);
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        RecordDuration(activity);
    }

    private static void RecordDuration(Activity? activity)
    {
        if (activity is null ||
            !QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
        {
            return;
        }

        QylHttpClientMetrics.RecordRequestDuration(activity.StartTimeUtc);
    }
}
