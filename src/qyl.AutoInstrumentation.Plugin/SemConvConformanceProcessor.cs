using System.Diagnostics;
using OpenTelemetry;

namespace Qyl.AutoInstrumentation.Plugin;

/// <summary>
/// M2 conformance check: every emitted attribute key MUST exist in the qyl semconv registry.
///
/// The known-key set is a PLACEHOLDER seeded from the M1 golden span — it will be replaced by
/// the generated constants from the qyl semconv source generator (blueprint T003).
///
/// Safety invariants (blueprint §T031):
///   - Never throws into the app (instrumentation errors are swallowed).
///   - Never writes to stdout/stderr (would violate Gate B no-behavior-change).
///   - Never mutates the activity (would violate Gate A golden-OTLP).
/// The verdict is side-channelled to QYL_CONFORMANCE_LOG (default: temp dir).
/// </summary>
internal sealed class SemConvConformanceProcessor : BaseProcessor<Activity>
{
    // PLACEHOLDER — replace with generated semconv constants (T003).
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "http.request.method",
        "http.response.status_code",
        "url.full",
        "url.scheme",
        "server.address",
        "server.port",
        "network.protocol.version",
    };

    private static readonly string LogPath =
        Environment.GetEnvironmentVariable("QYL_CONFORMANCE_LOG")
        ?? Path.Combine(Path.GetTempPath(), "qyl-conformance.log");

    public override void OnEnd(Activity activity)
    {
        try
        {
            foreach (var tag in activity.TagObjects)
            {
                var verdict = KnownKeys.Contains(tag.Key) ? "OK     " : "UNKNOWN";
                File.AppendAllText(LogPath, $"{verdict} {activity.Kind} {activity.DisplayName} :: {tag.Key}\n");
            }
        }
        catch
        {
            // Instrumentation must be invisible — never let a conformance-check failure reach the app.
        }
    }
}
