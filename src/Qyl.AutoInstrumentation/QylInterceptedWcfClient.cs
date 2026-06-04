using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedWcfClient
{

    public static Activity? StartActivity(string clientType, string methodName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.WcfClient))
            return null;

        var activity = QylActivitySource.Source.StartActivity("WCF CLIENT", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.RpcWcfClient);
        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemDotNetWcf);
        activity.SetTag(QylSemanticAttributes.RpcService, clientType);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
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
