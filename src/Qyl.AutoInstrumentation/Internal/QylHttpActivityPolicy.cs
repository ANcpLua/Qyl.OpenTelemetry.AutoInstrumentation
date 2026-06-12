using System.Diagnostics;

namespace Qyl.AutoInstrumentation.Internal;

internal static class QylHttpActivityPolicy
{
    public static Activity? StartClientActivity(
        string instrumentationDomain,
        string method,
        Uri? requestUri,
        string? rawRequestUri)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.HttpClient,
            QylActivityNames.HttpClient(method),
            ActivityKind.Client,
            instrumentationDomain);
        if (activity is null)
            return null;

        SetRequestMethod(activity, method);
        if (requestUri is not null)
            SetClientUrl(activity, requestUri, rawRequestUri);

        return activity;
    }

    public static Activity? StartServerActivity(
        string method,
        string? route,
        string? path,
        string? query)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.AspNetCore,
            QylActivityNames.HttpServer(method, route),
            ActivityKind.Server,
            QylInstrumentationDomains.AspNetCoreServer);
        if (activity is null)
            return null;

        SetRequestMethod(activity, method);
        if (path is not null)
            activity.SetTag(QylSemanticAttributes.UrlPath, path);
        if (!string.IsNullOrEmpty(query))
            QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);
        if (route is not null)
            activity.SetTag(QylSemanticAttributes.HttpRoute, route);

        return activity;
    }

    public static void SetResponseStatus(Activity activity, int statusCode, int errorStatusCodeFloor)
    {
        SetResponseStatus(activity, statusCode);
        if (statusCode >= errorStatusCodeFloor)
            QylActivityStatus.RecordError(activity, statusCode);
    }

    public static void SetResponseStatus(Activity activity, int statusCode)
        => activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, statusCode);

    private static void SetRequestMethod(Activity activity, string method)
        => activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);

    private static void SetClientUrl(Activity activity, Uri requestUri, string? rawRequestUri)
    {
        if (requestUri.IsAbsoluteUri)
        {
            activity.SetTag(QylSemanticAttributes.ServerAddress, requestUri.Host);
            if (!requestUri.IsDefaultPort)
                activity.SetTag(QylSemanticAttributes.ServerPort, requestUri.Port);
        }

        var urlFull = requestUri.IsAbsoluteUri ? requestUri.ToString() : rawRequestUri ?? requestUri.ToString();
        QylSensitiveCapturePolicy.SetHttpClientUrlFull(activity, urlFull);
    }
}
