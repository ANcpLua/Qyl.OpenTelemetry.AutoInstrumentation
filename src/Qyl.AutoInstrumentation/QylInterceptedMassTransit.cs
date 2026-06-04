using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedMassTransit
{
    private const string MassTransitDomain = "messaging.masstransit";

    public static Activity? StartActivity(string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.MassTransit))
            return null;

        var operation = string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? "send"
            : "publish";

        var activity = QylActivitySource.Source.StartActivity("MassTransit " + operation, ActivityKind.Producer);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, MassTransitDomain);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, "masstransit");
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operation);
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
