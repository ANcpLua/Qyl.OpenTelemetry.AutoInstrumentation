using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.AspNetCore;

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
        if (!StringComparer.Ordinal.Equals(name, "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop") ||
            QylAspNetCoreOwnership.MiddlewareRegistered)
            return;

        var method = HttpSemantics.NormalizeMethod(
            AspNetCorePayloadReader.GetMethod(payload) ??
            DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.HttpRequestMethod),
            out var originalMethod);
        var route = AspNetCorePayloadReader.GetRoute(payload) ??
                    DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.HttpRoute);
        var path = AspNetCorePayloadReader.GetPath(payload) ??
                   DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.UrlPath);
        var statusCode = AspNetCorePayloadReader.GetStatusCode(payload) ??
                         DiagnosticPayloadReader.GetInt32(payload, QylSemanticAttributes.HttpResponseStatusCode);
        var errorType = DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.ErrorType);

        using var activity = QylActivitySource.StartAtAmbientStart(QylSpanNames.HttpServer(method, route), ActivityKind.Server);

        SemanticTagWriter.Set(activity, QylSemanticAttributes.QylInstrumentationDomain, QylAttributes.InstrumentationDomainValues.AspNetCoreServer);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.HttpRequestMethod, method);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.HttpRequestMethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.HttpRoute, route);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.UrlPath, path);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.UrlScheme, AspNetCorePayloadReader.GetScheme(payload));

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
