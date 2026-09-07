using System.Net.Sockets;

namespace Qyl;

/// <summary>
/// Zero-config local collector discovery. Probes the conventional qyl endpoints once per process
/// and caches the result; the standard OTLP environment variables always take precedence and are
/// handled by the exporter itself, so discovery only runs when nothing is configured.
/// </summary>
/// <remarks>
/// The probe costs four 100 ms connect attempts plus a DNS lookup for <c>qyl</c> when nothing is
/// listening, so it never runs on the caller's thread. <see cref="Start"/> hands back a task the
/// exporter's options callback awaits when the OpenTelemetry SDK builds its pipeline — after the
/// host has finished wiring itself — and <c>AddQyl()</c> itself touches no socket.
/// </remarks>
internal static class CollectorDiscovery
{
    private static readonly Lazy<Task<Uri?>> s_probe =
        new(static () => Task.Run(ProbeForCollector), LazyThreadSafetyMode.ExecutionAndPublication);

    // OTLP/HTTP first: it works over HTTP/1.1 everywhere, while plaintext gRPC needs an explicit
    // HTTP/2-without-TLS arrangement. The "qyl" hostname covers the container-network default.
    private static readonly (string Host, int Port)[] s_probeTargets =
    [
        ("localhost", 4318),
        ("localhost", 4317),
        ("qyl", 4318),
        ("qyl", 4317)
    ];

    /// <summary>Starts the one process-wide probe, or returns the running one. Never blocks.</summary>
    internal static Task<Uri?> Start() => s_probe.Value;

    /// <summary>
    /// The probe's answer, waited for at most <paramref name="timeout"/>. Called from the exporter's
    /// options callback, which the SDK invokes when it builds the pipeline; a probe that has not
    /// finished by then yields no endpoint rather than holding the build open.
    /// </summary>
    internal static Uri? WaitForEndpoint(Task<Uri?> probe, TimeSpan timeout)
        => probe.Wait(timeout) ? probe.GetAwaiter().GetResult() : null;

    private static Uri? ProbeForCollector()
    {
        // QYL_ENDPOINT names a collector without claiming OTEL_EXPORTER_OTLP_ENDPOINT, which every
        // OTLP exporter in the process would honor. Discovery only runs when the standard variable
        // is unset, so this stays the documented fallback rather than an override of it. An
        // unparseable value means "configured, but wrong" — probing on would silently export
        // somewhere the operator did not ask for.
        var configured = Environment.GetEnvironmentVariable("QYL_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri) ? configuredUri : null;

        foreach (var (host, port) in s_probeTargets)
        {
            if (TcpProbe(host, port))
                return new Uri($"http://{host}:{port}");
        }

        return null;
    }

    private static bool TcpProbe(string host, int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var result = socket.BeginConnect(host, port, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));

            if (connected && socket.Connected)
            {
                socket.EndConnect(result);
                return true;
            }

            return false;
        }
        catch (SocketException)
        {
            // Connection refused / host unreachable / DNS failure — nothing is listening there.
            return false;
        }
    }
}
