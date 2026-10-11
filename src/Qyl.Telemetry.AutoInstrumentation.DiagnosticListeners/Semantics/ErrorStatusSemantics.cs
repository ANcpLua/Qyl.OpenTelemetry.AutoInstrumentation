using System.Diagnostics;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class ErrorStatusSemantics
{
    public static void SetError(Activity? activity, string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType))
            return;

        SemanticTagWriter.Set(
            activity,
            ErrorAttributes.Type,
            errorType);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
