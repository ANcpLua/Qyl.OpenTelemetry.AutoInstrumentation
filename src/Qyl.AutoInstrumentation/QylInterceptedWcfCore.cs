using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedWcfCore
{

    public static Activity? StartActivity(string serviceName, string contractName, string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.WcfCore))
            return null;

        var activity = QylActivitySource.Source.StartActivity("CoreWCF SERVER", ActivityKind.Server);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.RpcWcfCore);
        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemDotNetWcf);
        activity.SetTag(QylSemanticAttributes.RpcService, string.IsNullOrEmpty(contractName) ? serviceName : contractName);
        activity.SetTag(QylSemanticAttributes.RpcMethod, operationName);
        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
