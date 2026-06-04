using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedQuartz
{
    private const string QuartzDomain = "job.quartz";

    public static Activity? StartActivity(string jobType)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Quartz))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Quartz execute", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QuartzDomain);
        activity.SetTag(QylSemanticAttributes.RpcSystem, "quartz");
        activity.SetTag(QylSemanticAttributes.RpcService, jobType);
        activity.SetTag(QylSemanticAttributes.RpcMethod, "Execute");
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
