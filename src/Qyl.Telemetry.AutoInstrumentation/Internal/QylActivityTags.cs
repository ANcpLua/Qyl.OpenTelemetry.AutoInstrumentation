using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylActivityTags
{
    public static void SetMessaging(
        Activity activity,
        string system,
        string operationType,
        string operationName,
        string? destination)
    {
        activity.SetTag(QylSemanticAttributes.MessagingSystem, system);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, operationType);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operationName);
        if (destination is not null)
            activity.SetTag(QylSemanticAttributes.MessagingDestinationName, destination);
    }

    public static void SetDb(
        Activity activity,
        string systemName,
        string? operationName,
        string? querySummary)
    {
        activity.SetTag(QylSemanticAttributes.DbSystemName, systemName);
        SetDbOperation(activity, operationName, querySummary);
    }

    public static void SetDbOperation(
        Activity activity,
        string? operationName,
        string? querySummary)
    {
        if (operationName is not null)
            activity.SetTag(QylSemanticAttributes.DbOperationName, operationName);
        if (querySummary is not null)
            activity.SetTag(QylSemanticAttributes.DbQuerySummary, querySummary);
    }

    public static void SetRpc(
        Activity activity,
        string system,
        string method)
    {
        activity.SetTag(QylSemanticAttributes.RpcSystem, system);
        activity.SetTag(QylSemanticAttributes.RpcMethod, method);
    }

    public static void SetGraphQlOperationName(Activity activity, string operationName)
        => activity.SetTag(QylSemanticAttributes.GraphQlOperationName, operationName);

    public static void SetGraphQlOperationType(Activity activity, string operationType)
        => activity.SetTag(QylSemanticAttributes.GraphQlOperationType, operationType);
}
