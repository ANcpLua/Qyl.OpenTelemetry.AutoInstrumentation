using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylHttpActivityPolicy
{
    public static Activity? StartClientActivity(
        string instrumentationDomain,
        string method,
        string? methodOriginal,
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

        SetRequestMethod(activity, method, methodOriginal);
        if (requestUri is not null)
            SetClientUrl(activity, requestUri, rawRequestUri);

        return activity;
    }

    public static Activity? StartServerActivity(
        string method,
        string? methodOriginal,
        string? route,
        string? path,
        string? query,
        string? scheme)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.AspNetCore,
            QylActivityNames.HttpServer(method, route),
            ActivityKind.Server,
            QylInstrumentationDomains.AspNetCoreServer);
        if (activity is null)
            return null;

        SetRequestMethod(activity, method, methodOriginal);
        if (!string.IsNullOrEmpty(scheme))
            activity.SetTag(QylSemanticAttributes.UrlScheme, scheme);
        if (path is not null)
            activity.SetTag(QylSemanticAttributes.UrlPath, path);
        if (!string.IsNullOrEmpty(query))
            QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);
        if (route is not null)
            activity.SetTag(QylSemanticAttributes.HttpRoute, route);

        return activity;
    }

    // Backfills the route template and refines the span name once routing has resolved the endpoint. The
    // server-span middleware can run outside routing (registered via IStartupFilter), where the endpoint is
    // not yet available at activity start; call this after the pipeline has run. No-op when the route is
    // unknown or was already captured (the per-endpoint interceptor path sets it at start).
    public static void BackfillServerRoute(Activity activity, string method, string? route)
    {
        if (string.IsNullOrEmpty(route) || activity.GetTagItem(QylSemanticAttributes.HttpRoute) is not null)
            return;

        activity.SetTag(QylSemanticAttributes.HttpRoute, route);
        activity.DisplayName = QylActivityNames.HttpServer(method, route);
    }

    public static void SetResponseStatus(Activity activity, int statusCode, int errorStatusCodeFloor)
    {
        activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, statusCode);
        if (statusCode >= errorStatusCodeFloor)
            QylActivityStatus.RecordError(activity, statusCode);
    }

    private static void SetRequestMethod(Activity activity, string method, string? methodOriginal)
    {
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);
        if (!string.IsNullOrEmpty(methodOriginal))
            activity.SetTag(QylSemanticAttributes.HttpRequestMethodOriginal, methodOriginal);
    }

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
