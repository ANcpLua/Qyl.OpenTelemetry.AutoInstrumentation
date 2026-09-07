using System.Diagnostics;
using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
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

    // ASP.NET Core's own server activity carries the qyl domain and the request half of the HTTP
    // conventions. Every write fills only what the activity does not already have: the runtime creates
    // it empty today, and a tag another component wrote is that component's to own.
    public static void SetServerRequest(
        Activity activity,
        string method,
        string? methodOriginal,
        string? path,
        string? query,
        string? scheme)
    {
        SetIfAbsent(activity, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.AspNetCoreServer);
        if (activity.GetTagItem(HttpAttributes.RequestMethod) is null)
            SetRequestMethod(activity, method, methodOriginal);
        if (!string.IsNullOrEmpty(scheme))
            SetIfAbsent(activity, UrlAttributes.Scheme, scheme);
        if (path is not null)
            SetIfAbsent(activity, UrlAttributes.Path, path);
        if (!string.IsNullOrEmpty(query) && activity.GetTagItem(UrlAttributes.Query) is null)
            QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);
    }

    // Records the route template and names the span after it, once routing has resolved the endpoint.
    // The enriching middleware runs outside routing (registered via IStartupFilter), so the endpoint is
    // unknown while the request goes in; call this on the way out. A request that resolved no endpoint
    // carries no route — a 404, a static file, anything short-circuited ahead of routing — and is named
    // after its method alone rather than left on the framework's raw operation name. The display name is
    // refined only while it is still that operation name, so a name another component chose survives.
    public static void SetServerRoute(Activity activity, string method, string? route)
    {
        if (!string.IsNullOrEmpty(route) && activity.GetTagItem(HttpAttributes.Route) is null)
            activity.SetTag(HttpAttributes.Route, route);

        if (StringComparer.Ordinal.Equals(activity.DisplayName, activity.OperationName))
            activity.DisplayName = QylSpanNames.HttpServer(method, activity.GetTagItem(HttpAttributes.Route) as string);
    }

    public static void SetServerResponseStatus(Activity activity, int statusCode)
    {
        if (activity.GetTagItem(HttpAttributes.ResponseStatusCode) is not null)
            return;

        activity.SetTag(HttpAttributes.ResponseStatusCode, statusCode);
        if (statusCode >= 500 && activity.GetTagItem(ErrorAttributes.Type) is null)
            QylActivityStatus.RecordError(activity, statusCode);
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

    private static void SetIfAbsent(Activity activity, string key, string value)
    {
        if (activity.GetTagItem(key) is null)
            activity.SetTag(key, value);
    }

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
