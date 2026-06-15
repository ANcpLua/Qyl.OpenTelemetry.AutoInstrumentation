using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class SemanticTagWriter
{
    public static void Set(Activity? activity, SemanticAttributeDefinition attribute, string? value)
    {
        if (activity is null ||
            string.IsNullOrWhiteSpace(value) ||
            !SemanticAttributePolicy.Current.ShouldWrite(attribute))
        {
            return;
        }

        activity.SetTag(attribute.Key, value);
    }

    public static void Set(Activity? activity, SemanticAttributeDefinition attribute, int? value)
    {
        if (activity is null ||
            value is null ||
            !SemanticAttributePolicy.Current.ShouldWrite(attribute))
        {
            return;
        }

        activity.SetTag(attribute.Key, value.Value);
    }
}
