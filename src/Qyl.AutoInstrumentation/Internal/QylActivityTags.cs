using System.Diagnostics;

namespace Qyl.AutoInstrumentation.Internal;

internal static class QylActivityTags
{
    public static void SetMessaging(
        Activity activity,
        string system,
        string operationType,
        string operationName)
    {
        activity.SetTag(QylSemanticAttributes.MessagingSystem, system);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, operationType);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operationName);
    }

    public static void SetDb(
        Activity activity,
        string systemName,
        string operationName,
        string querySummary)
    {
        activity.SetTag(QylSemanticAttributes.DbSystemName, systemName);
        activity.SetTag(QylSemanticAttributes.DbOperationName, operationName);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, querySummary);
    }

    public static void SetRpc(
        Activity activity,
        string system,
        string service,
        string method)
    {
        activity.SetTag(QylSemanticAttributes.RpcSystem, system);
        activity.SetTag(QylSemanticAttributes.RpcService, service);
        activity.SetTag(QylSemanticAttributes.RpcMethod, method);
    }

    public static void SetGraphQlOperationName(Activity activity, string operationName)
        => activity.SetTag(QylSemanticAttributes.GraphQlOperationName, operationName);

    public static void SetLogSeverity(Activity activity, string severity)
        => activity.SetTag(QylSemanticAttributes.LogSeverity, severity);
}
