using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Redis.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedRedis);</code></example>
public static class QylInterceptedRedis
{

    /// <summary>Runs the Start Command Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartCommandActivity(string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.StackExchangeRedis))
            return null;

        var activity = QylActivitySource.StartActivity("Redis command", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbRedis);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemRedis);
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
