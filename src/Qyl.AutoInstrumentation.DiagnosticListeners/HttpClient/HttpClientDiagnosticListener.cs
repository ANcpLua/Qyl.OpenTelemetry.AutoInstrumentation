using System.Diagnostics;
using Qyl.AutoInstrumentation;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.HttpClient;

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
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.http.client") &&
            !StringComparer.Ordinal.Equals(name, "System.Net.Http.HttpRequestOut.Stop"))
        {
            return;
        }

        var method = DiagnosticPayloadReader.GetString(payload, "http.request.method", "GET");
        var url = DiagnosticPayloadReader.GetString(payload, "url.full", "http://qyl.local/client");
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "qyl.local");
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, "http.response.status_code", 200);

        using var activity = QylActivitySource.Source.StartActivity($"HTTP {method}", ActivityKind.Client);
        activity?.SetTag("qyl.instrumentation.domain", "http.client");
        activity?.SetTag("http.request.method", method);
        activity?.SetTag("url.full", url);
        activity?.SetTag("server.address", serverAddress);
        activity?.SetTag("http.response.status_code", statusCode);
    }
}
