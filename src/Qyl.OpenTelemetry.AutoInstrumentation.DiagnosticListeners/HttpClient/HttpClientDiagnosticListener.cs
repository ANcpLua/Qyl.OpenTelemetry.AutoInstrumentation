using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.HttpClient;

/// <summary>
/// Subscribes to <c>HttpHandlerDiagnosticListener</c> — the listener emitted by
/// <c>System.Net.Http.HttpClient</c>'s <see cref="System.Diagnostics.DiagnosticSource"/> integration.
/// AOT-native replacement for substrate's HttpClient CallTarget integration (M1 walking skeleton
/// in the substrate era was this exact span; the new M1 will re-prove it through this path).
/// </summary>
public sealed class HttpClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "HttpHandlerDiagnosticListener";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.HttpClient;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.http.client") &&
            !StringComparer.Ordinal.Equals(name, "System.Net.Http.HttpRequestOut.Stop"))
        {
            return;
        }

        var method = HttpSemantics.NormalizeMethod(
            DiagnosticPayloadReader.GetString(payload, "http.request.method", "http.method"),
            out var originalMethod);
        var url = DiagnosticPayloadReader.GetString(payload, "url.full", "http.url");
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "peer.hostname");
        var serverPort = DiagnosticPayloadReader.GetInt32(payload, "server.port", "peer.port");
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, "http.response.status_code", "http.status_code");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.Source.StartActivity(QylActivityNames.HttpClient(method), ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpClient);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethod, method);
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpRequestMethodOriginal, originalMethod);
        HttpSemantics.SetUrlTags(activity, url, serverAddress, serverPort);
        HttpSemantics.SetStatus(activity, ActivityKind.Client, statusCode, errorType);
    }
}
