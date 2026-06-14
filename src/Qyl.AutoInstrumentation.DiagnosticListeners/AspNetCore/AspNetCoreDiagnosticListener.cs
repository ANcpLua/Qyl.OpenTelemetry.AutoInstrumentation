using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.AspNetCore;

/// <summary>
/// Subscribes to <c>Microsoft.AspNetCore</c> — the listener emitted by Kestrel + the MVC pipeline.
/// Provides HTTP SERVER spans without any per-request middleware injection.
/// </summary>
public sealed class AspNetCoreDiagnosticListener : DiagnosticListenerSubscriber
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
        if (!StringComparer.Ordinal.Equals(name, "qyl.http.server") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop"))
        {
            return;
        }

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

        using var activity = QylActivitySource.Source.StartActivity(QylActivityNames.HttpServer(method, route), ActivityKind.Server);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpServer);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethod, method);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRoute, route);
        SemanticTagWriter.Set(activity, SemanticAttributes.UrlPath, path);
        HttpSemantics.SetStatus(activity, ActivityKind.Server, statusCode, errorType);
    }
}
