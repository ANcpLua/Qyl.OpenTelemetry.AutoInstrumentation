using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Wcf Core.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedWcfCore);</code></example>
public static class QylInterceptedWcfCore
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string serviceName, string contractName, string operationName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.WcfCore,
            QylActivityNames.CoreWcfServer,
            ActivityKind.Server,
            QylInstrumentationDomains.RpcWcfCore);
        if (activity is null)
            return null;

        QylActivityTags.SetRpc(
            activity,
            QylSemanticAttributes.RpcSystemDotNetWcf,
            string.IsNullOrEmpty(contractName) ? serviceName : contractName,
            operationName);
        return activity;
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
