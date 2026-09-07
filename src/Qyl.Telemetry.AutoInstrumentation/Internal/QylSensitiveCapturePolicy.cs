using System.Data.Common;
using System.Diagnostics;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylSensitiveCapturePolicy
{
    public static void SetAspNetCoreUrlQuery(Activity activity, string query)
    {
        activity.SetTag(
            UrlAttributes.Query,
            QylAutoInstrumentationOptions.Current.AspNetCoreUrlQueryRedactionDisabled
                ? query
                : QylCaptureHelpers.RedactQueryValues(query));
    }

    public static void SetDbQueryText(Activity activity, DbCommand command, string instrumentationId)
    {
        if (!ShouldCaptureDbQueryText(command, instrumentationId))
            return;

        activity.SetTag(DbAttributes.QueryText, command.CommandText);
    }

    private static bool ShouldCaptureDbQueryText(DbCommand command, string instrumentationId)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        return StringComparer.Ordinal.Equals(instrumentationId, QylAutoInstrumentationIds.SqlClient)
            && QylAutoInstrumentationOptions.Current.SqlClientSetDbStatementForText;
    }
}
