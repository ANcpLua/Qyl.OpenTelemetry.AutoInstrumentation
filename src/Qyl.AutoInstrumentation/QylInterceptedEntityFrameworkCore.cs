using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Entity Framework Core.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedEntityFrameworkCore);</code></example>
public static class QylInterceptedEntityFrameworkCore
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.EntityFrameworkCore))
            return null;

        var activity = QylActivitySource.StartActivity(QylActivityNames.DbCommand(operationName), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbEfCore);
        activity.SetTag(QylSemanticAttributes.DbOperationName, operationName);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, operationName);
        return activity;
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
