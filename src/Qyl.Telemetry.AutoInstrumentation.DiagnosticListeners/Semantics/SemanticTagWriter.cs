using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class SemanticTagWriter
{
    public static void Set(Activity? activity, string key, string? value)
    {
        if (activity is null || string.IsNullOrWhiteSpace(value))
            return;

        activity.SetTag(key, value);
    }

    public static void Set(Activity? activity, string key, int? value)
    {
        if (activity is null || value is null)
            return;

        activity.SetTag(key, value.Value);
    }
}
