using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedAzure
{
    private const string AzureDomain = "azure.sdk";

    public static Activity? StartActivity(string clientType, string methodName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Azure))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Azure " + methodName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, AzureDomain);
        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemAzure);
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
