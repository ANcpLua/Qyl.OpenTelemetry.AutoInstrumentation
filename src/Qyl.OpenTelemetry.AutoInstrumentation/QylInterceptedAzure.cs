using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Azure.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedAzure
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string methodName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.Azure,
            QylActivityNames.AzureSdk,
            ActivityKind.Client,
            QylInstrumentationDomains.AzureSdk);
        // The span name stays the stable low-cardinality "Azure SDK"; the specific method goes
        // on code.function.name.
        if (activity is not null && !string.IsNullOrEmpty(methodName))
            activity.SetTag(QylSemanticAttributes.CodeFunctionName, methodName);
        return activity;
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
