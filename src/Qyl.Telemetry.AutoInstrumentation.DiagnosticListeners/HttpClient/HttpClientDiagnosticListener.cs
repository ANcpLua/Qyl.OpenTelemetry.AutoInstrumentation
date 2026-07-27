using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Incubating.Names.QylTelemetryNames;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.HttpClient;

/// <summary>
/// Subscribes to <c>HttpHandlerDiagnosticListener</c>, the listener emitted by
/// <c>System.Net.Http.HttpClient</c>'s <see cref="System.Diagnostics.DiagnosticSource"/> integration,
/// and emits bounded HttpClient telemetry through the managed AOT-compatible path.
/// </summary>
internal sealed class HttpClientDiagnosticListener : QylDiagnosticListenerSubscriber
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
        if (!StringComparer.Ordinal.Equals(name, QylTelemetryNames.Events.QylHttpClient) &&
            !StringComparer.Ordinal.Equals(name, "System.Net.Http.HttpRequestOut.Stop"))
        {
            return;
        }

        var method = HttpSemantics.NormalizeMethod(
            DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.HttpRequestMethod),
            out var originalMethod);
        var url = DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.UrlFull);
        var serverAddress = DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.ServerAddress);
        var serverPort = DiagnosticPayloadReader.GetInt32(payload, QylSemanticAttributes.ServerPort);
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, QylSemanticAttributes.HttpResponseStatusCode);
        var errorType = DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.ErrorType);

        using var activity = QylActivitySource.StartAtAmbientStart(QylActivityNames.HttpClient(method), ActivityKind.Client);

        SemanticTagWriter.Set(activity, QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.HttpClient);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.HttpRequestMethod, method);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.HttpRequestMethodOriginal, originalMethod);
        HttpSemantics.SetUrlTags(activity, url, serverAddress, serverPort);
        HttpSemantics.SetStatus(activity, ActivityKind.Client, statusCode, errorType);
    }
}
