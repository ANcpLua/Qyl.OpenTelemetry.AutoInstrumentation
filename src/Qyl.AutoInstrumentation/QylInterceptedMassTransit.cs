using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedMassTransit
{

    public static Activity? StartActivity(string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.MassTransit))
            return null;

        var operation = string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;

        var activity = QylActivitySource.Source.StartActivity("MassTransit message", ActivityKind.Producer);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.MessagingMassTransit);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemMassTransit);
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
