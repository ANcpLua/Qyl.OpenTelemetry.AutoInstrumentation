using System.Diagnostics;
using Qyl.AutoInstrumentation;

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
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.http.server") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop"))
        {
            return;
        }

        var method = DiagnosticPayloadReader.GetString(payload, "http.request.method", "GET");
        var route = DiagnosticPayloadReader.GetString(payload, "http.route", "/qyl/{id}");
        var path = DiagnosticPayloadReader.GetString(payload, "url.path", "/qyl/demo");
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, "http.response.status_code", 200);

        using var activity = QylActivitySource.Source.StartActivity($"HTTP SERVER {method}", ActivityKind.Server);
        activity?.SetTag("qyl.instrumentation.domain", "http.server");
        activity?.SetTag("http.request.method", method);
        activity?.SetTag("http.route", route);
        activity?.SetTag("url.path", path);
        activity?.SetTag("http.response.status_code", statusCode);
    }
}
