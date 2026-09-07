using System.Diagnostics;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;

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
        activity.SetTag(MessagingAttributes.System, system);
        activity.SetTag(MessagingAttributes.OperationType, operationType);
        activity.SetTag(MessagingAttributes.OperationName, operationName);
        if (destination is not null)
            activity.SetTag(MessagingAttributes.DestinationName, destination);
    }

    public static void SetDb(
        Activity activity,
        string systemName,
        string? operationName,
        string? querySummary)
    {
        activity.SetTag(DbAttributes.SystemName, systemName);
        SetDbOperation(activity, operationName, querySummary);
    }

    public static void SetDbOperation(
        Activity activity,
        string? operationName,
        string? querySummary)
    {
        if (operationName is not null)
            activity.SetTag(DbAttributes.OperationName, operationName);
        if (querySummary is not null)
            activity.SetTag(DbAttributes.QuerySummary, querySummary);
    }

    public static void SetRpc(
        Activity activity,
        string system,
        string method)
    {
        activity.SetTag(RpcAttributes.SystemName, system);
        activity.SetTag(RpcAttributes.Method, method);
    }
}
