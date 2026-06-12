using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Wcf Client.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedWcfClient);</code></example>
public static class QylInterceptedWcfClient
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string clientType, string methodName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.WcfClient,
            QylActivityNames.WcfClient,
            ActivityKind.Client,
            QylInstrumentationDomains.RpcWcfClient);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemDotNetWcf);
        activity.SetTag(QylSemanticAttributes.RpcService, clientType);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
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
