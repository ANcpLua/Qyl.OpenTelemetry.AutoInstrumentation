using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.AspNetCore;

/// <summary>
/// Subscribes to <c>Microsoft.AspNetCore</c> — the listener emitted by Kestrel + the MVC pipeline.
/// Provides HTTP SERVER spans without any per-request middleware injection.
/// </summary>
internal sealed class AspNetCoreDiagnosticListener : QylDiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Microsoft.AspNetCore";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.AspNetCore;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (StringComparer.Ordinal.Equals(name, "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start"))
        {
            var ambient = Activity.Current;
            if (ambient is not null)
                ambient.ActivityTraceFlags |= ActivityTraceFlags.Recorded;

            return;
        }

        if (!StringComparer.Ordinal.Equals(name, "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop") ||
            QylAspNetCoreOwnership.MiddlewareRegistered)
            return;

        var method = HttpSemantics.NormalizeMethod(
            AspNetCorePayloadReader.GetMethod(payload) ??
            DiagnosticPayloadReader.GetString(payload, "http.request.method", "http.method"),
            out var originalMethod);
        var route = AspNetCorePayloadReader.GetRoute(payload) ??
                    DiagnosticPayloadReader.GetString(payload, "http.route");
        var path = AspNetCorePayloadReader.GetPath(payload) ??
                   DiagnosticPayloadReader.GetString(payload, "url.path", "http.target");
        var statusCode = AspNetCorePayloadReader.GetStatusCode(payload) ??
                         DiagnosticPayloadReader.GetInt32(payload, "http.response.status_code", "http.status_code");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.StartAtAmbientStart(QylActivityNames.HttpServer(method, route), ActivityKind.Server);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpServer);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethod, method);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRoute, route);
        SemanticTagWriter.Set(activity, SemanticAttributes.UrlPath, path);

        // Option parity with the explicit middleware lane: url.query obeys the ASP.NET Core
        // redaction control; header capture obeys the configured capture lists.
        if (activity is not null)
        {
            var query = AspNetCorePayloadReader.GetQuery(payload);
            if (!string.IsNullOrEmpty(query))
                Internal.QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);

            var options = QylAutoInstrumentationOptions.Current;
            if (AspNetCorePayloadReader.GetRequestHeaders(payload) is { } requestHeaders)
                Internal.QylCaptureHelpers.SetRequestHeaders(activity, options.AspNetCoreCapturedRequestHeaderMap, requestHeaders);
            if (AspNetCorePayloadReader.GetResponseHeaders(payload) is { } responseHeaders)
                Internal.QylCaptureHelpers.SetRequestHeaders(activity, options.AspNetCoreCapturedResponseHeaderMap, responseHeaders);
        }

        HttpSemantics.SetStatus(activity, ActivityKind.Server, statusCode, errorType);
    }
}
