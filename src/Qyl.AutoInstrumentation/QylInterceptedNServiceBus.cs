using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedNServiceBus
{

    public static Activity? StartActivity(string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.NServiceBus))
            return null;

        var operation = string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;

        var activity = QylActivitySource.StartActivity("NServiceBus message", ActivityKind.Producer);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.MessagingNServiceBus);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemNServiceBus);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend);
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
