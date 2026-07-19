using System.Diagnostics;
using System.Globalization;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylActivityStatus
{
    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.ErrorType, GetErrorTypeName(exception.GetType()));
        activity.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>Namespace-qualified type name without generic-argument expansion, so generic
    /// exceptions stay bounded ("Confluent.Kafka.ProduceException`2") instead of the
    /// assembly-qualified FullName explosion.</summary>
    private static string GetErrorTypeName(Type type)
        => !type.IsGenericType && type.FullName is { } fullName
            ? fullName
            : type.Namespace is { } ns ? ns + "." + type.Name : type.Name;

    public static void RecordError(Activity? activity, int statusCode)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.ErrorType, statusCode.ToString(CultureInfo.InvariantCulture));
        activity.SetStatus(ActivityStatusCode.Error);
    }
}
