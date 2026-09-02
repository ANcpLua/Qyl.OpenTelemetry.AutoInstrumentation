using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

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
            DiagnosticPayloadReader.GetString(payload, HttpAttributes.RequestMethod),
            out var originalMethod);
        var url = DiagnosticPayloadReader.GetString(payload, UrlAttributes.Full);
        var serverAddress = DiagnosticPayloadReader.GetString(payload, ServerAttributes.Address);
        var serverPort = DiagnosticPayloadReader.GetInt32(payload, ServerAttributes.Port);
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, HttpAttributes.ResponseStatusCode);
        var errorType = DiagnosticPayloadReader.GetString(payload, ErrorAttributes.Type);

        using var activity = QylActivitySource.StartAtAmbientStart(QylSpanNames.Http(method), ActivityKind.Client);

        SemanticTagWriter.Set(activity, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.HttpClient);
        SemanticTagWriter.Set(activity, HttpAttributes.RequestMethod, method);
        SemanticTagWriter.Set(activity, HttpAttributes.RequestMethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, NetworkAttributes.ProtocolVersion, DiagnosticPayloadReader.GetString(payload, NetworkAttributes.ProtocolVersion));
        HttpSemantics.SetUrlTags(activity, url, serverAddress, serverPort);
        HttpSemantics.SetStatus(activity, ActivityKind.Client, statusCode, errorType);
    }
}
