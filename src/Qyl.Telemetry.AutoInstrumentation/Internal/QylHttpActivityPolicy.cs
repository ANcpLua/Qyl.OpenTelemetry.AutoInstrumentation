using System.Diagnostics;
using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

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
            QylSpanNames.Http(method),
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
            QylSpanNames.HttpServer(method, route),
            ActivityKind.Server,
            QylAttributes.InstrumentationDomainValues.AspNetCoreServer);
        if (activity is null)
            return null;

        SetRequestMethod(activity, method, methodOriginal);
        if (!string.IsNullOrEmpty(scheme))
            activity.SetTag(UrlAttributes.Scheme, scheme);
        if (path is not null)
            activity.SetTag(UrlAttributes.Path, path);
        if (!string.IsNullOrEmpty(query))
            QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);
        if (route is not null)
            activity.SetTag(HttpAttributes.Route, route);

        return activity;
    }

    // Backfills the route template and refines the span name once routing has resolved the endpoint. The
    // server-span middleware can run outside routing (registered via IStartupFilter), where the endpoint is
    // not yet available at activity start; call this after the pipeline has run. No-op when the route is
    // unknown or was already captured (the per-endpoint interceptor path sets it at start).
    public static void BackfillServerRoute(Activity activity, string method, string? route)
    {
        if (string.IsNullOrEmpty(route) || activity.GetTagItem(HttpAttributes.Route) is not null)
            return;

        activity.SetTag(HttpAttributes.Route, route);
        activity.DisplayName = QylSpanNames.HttpServer(method, route);
    }

    public static void SetResponseStatus(Activity activity, int statusCode, int errorStatusCodeFloor)
    {
        activity.SetTag(HttpAttributes.ResponseStatusCode, statusCode);
        if (statusCode >= errorStatusCodeFloor)
            QylActivityStatus.RecordError(activity, statusCode);
    }

    public static void SetProtocolVersion(Activity activity, Version version)
        => activity.SetTag(
            NetworkAttributes.ProtocolVersion,
            version.Major >= 2 && version.Minor is 0 ? version.Major.ToString(CultureInfo.InvariantCulture) : version.ToString(2));

    private static void SetRequestMethod(Activity activity, string method, string? methodOriginal)
    {
        activity.SetTag(HttpAttributes.RequestMethod, method);
        if (!string.IsNullOrEmpty(methodOriginal))
            activity.SetTag(HttpAttributes.RequestMethodOriginal, methodOriginal);
    }

    private static void SetClientUrl(Activity activity, Uri requestUri, string? rawRequestUri)
    {
        if (requestUri.IsAbsoluteUri)
        {
            activity.SetTag(ServerAttributes.Address, requestUri.Host);
            if (!requestUri.IsDefaultPort)
                activity.SetTag(ServerAttributes.Port, requestUri.Port);
        }

        var urlFull = requestUri.IsAbsoluteUri ? requestUri.ToString() : rawRequestUri ?? requestUri.ToString();
        QylSensitiveCapturePolicy.SetHttpClientUrlFull(activity, urlFull);
    }
}
