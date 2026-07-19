using System.Diagnostics;
using System.Globalization;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylActivityStatus
{
    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error);
    }

    public static void RecordError(Activity? activity, int statusCode)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.ErrorType, statusCode.ToString(CultureInfo.InvariantCulture));
        activity.SetStatus(ActivityStatusCode.Error);
    }
}
