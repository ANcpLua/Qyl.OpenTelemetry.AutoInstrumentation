using System.Net.Sockets;

namespace Qyl;

/// <summary>
/// Zero-config local collector discovery. Probes the conventional qyl endpoints once per process
/// and caches the result; the standard OTLP environment variables always take precedence and are
/// handled by the exporter itself, so discovery only runs when nothing is configured.
/// </summary>
internal static class CollectorDiscovery
{
    private static readonly Lazy<Uri?> s_cachedEndpoint =
        new(ProbeForCollector, LazyThreadSafetyMode.ExecutionAndPublication);

    // OTLP/HTTP first: it works over HTTP/1.1 everywhere, while plaintext gRPC needs an explicit
    // HTTP/2-without-TLS arrangement. The "qyl" hostname covers the container-network default.
    private static readonly (string Host, int Port)[] s_probeTargets =
    [
        ("localhost", 4318),
        ("localhost", 4317),
        ("qyl", 4318),
        ("qyl", 4317)
    ];

    internal static Uri? DiscoverEndpoint() => s_cachedEndpoint.Value;

    private static Uri? ProbeForCollector()
    {
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
