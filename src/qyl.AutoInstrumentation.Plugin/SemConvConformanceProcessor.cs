using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;

namespace Qyl.AutoInstrumentation.Plugin;

/// <summary>
/// M3 conformance check: every emitted attribute key MUST exist in the qyl semconv registry.
///
/// The registry is built by REFLECTION over the Qyl.OpenTelemetry.SemanticConventions packages
/// (stable + Incubating) — no hardcoded keys. This promotes the generator to a runtime-enforced
/// invariant: qyl and the generator cannot drift.
///
/// Safety invariants (blueprint §T031):
///   - Never throws into the app (errors swallowed).
///   - Never writes to stdout/stderr (would violate Gate B no-behavior-change).
///   - Never mutates the activity (would violate Gate A golden-OTLP).
/// Verdicts side-channel to QYL_CONFORMANCE_LOG (default: temp dir).
/// </summary>
internal sealed class SemConvConformanceProcessor : BaseProcessor<Activity>
{
    private static readonly string LogPath =
        Environment.GetEnvironmentVariable("QYL_CONFORMANCE_LOG")
        ?? Path.Combine(Path.GetTempPath(), "qyl-conformance.log");

    private static readonly HashSet<string> KnownKeys = BuildRegistry();

    static SemConvConformanceProcessor()
    {
        // Self-test — proves the check DISCRIMINATES, independent of what the span emits:
        // a real key must be known; a synthetic key must be unknown. This is M3's "teeth".
        Log($"REGISTRY size={KnownKeys.Count}");
        Log($"SELFTEST known[http.request.method]={KnownKeys.Contains("http.request.method")}");
        Log($"SELFTEST unknown[qyl.__synthetic_unknown__]={KnownKeys.Contains("qyl.__synthetic_unknown__")}");
    }

    public override void OnEnd(Activity activity)
    {
        try
        {
            foreach (var tag in activity.TagObjects)
            {
                var verdict = KnownKeys.Contains(tag.Key) ? "OK     " : "UNKNOWN";
                Log($"{verdict} {activity.Kind} {activity.DisplayName} :: {tag.Key}");
            }
        }
        catch
        {
            // Instrumentation must be invisible — never let a conformance-check failure reach the app.
        }
    }

    private static HashSet<string> BuildRegistry()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "Qyl.OpenTelemetry.SemanticConventions",
            "Qyl.OpenTelemetry.SemanticConventions.Incubating",
        })
        {
            try { CollectKeys(Assembly.Load(name), keys); }
            catch { /* Incubating is optional; the stable surface alone is still a valid registry. */ }
        }
        return keys;
    }

    private static void CollectKeys(Assembly asm, HashSet<string> keys)
    {
        foreach (var type in asm.GetTypes())
        {
            // Keys live as const strings on the *Attributes group classes. Nested *Values classes
            // hold attribute VALUES (e.g. "aborted"), not keys — excluded by the name suffix.
            if (!type.Name.EndsWith("Attributes", StringComparison.Ordinal))
                continue;

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (f.FieldType != typeof(string)) continue;
                var value = f.IsLiteral ? (string?)f.GetRawConstantValue()
                          : f.IsInitOnly ? f.GetValue(null) as string
                          : null;
                if (!string.IsNullOrEmpty(value)) keys.Add(value!);
            }
        }
    }

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, line + "\n"); } catch { /* invisible */ }
    }
}
