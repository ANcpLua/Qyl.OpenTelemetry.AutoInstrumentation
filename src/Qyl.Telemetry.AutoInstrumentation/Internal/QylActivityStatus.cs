using System.Diagnostics;
using System.Globalization;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylActivityStatus
{
    /// <summary>
    /// Records a failure with <c>Spans.RecordFailure</c>, the call application code makes (ADR D4):
    /// status <see cref="ActivityStatusCode.Error"/> with the exception's message as the bounded
    /// description, <c>error.type</c>, and the exception event.
    /// </summary>
    /// <remarks>
    /// <c>RecordFailure</c> writes <see cref="Type.FullName"/>, which for a closed generic exception
    /// carries every type argument assembly-qualified, versions and public key tokens included. qyl
    /// keeps <c>error.type</c> bounded ("Confluent.Kafka.ProduceException`2") until the
    /// semantic-convention package does the same.
    /// </remarks>
    public static void RecordFailure(Activity activity, Exception exception)
    {
        activity.RecordFailure(exception);
        if (exception.GetType() is { IsGenericType: true } type)
            activity.SetTag(ErrorAttributes.Type, type.Namespace is { } ns ? ns + "." + type.Name : type.Name);
    }

    public static void RecordError(Activity? activity, int statusCode)
    {
        if (activity is null)
            return;

        activity.SetTag(ErrorAttributes.Type, statusCode.ToString(CultureInfo.InvariantCulture));
        activity.SetStatus(ActivityStatusCode.Error);
    }
}
