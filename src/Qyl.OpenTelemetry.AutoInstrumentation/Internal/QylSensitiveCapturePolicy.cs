using System.Data.Common;
using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylSensitiveCapturePolicy
{
    public static void SetAspNetCoreUrlQuery(Activity activity, string query)
    {
        activity.SetTag(
            QylSemanticAttributes.UrlQuery,
            QylAutoInstrumentationOptions.Current.AspNetCoreUrlQueryRedactionDisabled
                ? query
                : QylCaptureHelpers.RedactQueryValues(query));
    }

    public static void SetHttpClientUrlFull(Activity activity, string url)
    {
        activity.SetTag(
            QylSemanticAttributes.UrlFull,
            QylCaptureHelpers.FormatUrlFull(
                url,
                QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled));
    }

    public static void SetDbQueryText(Activity activity, DbCommand command, string instrumentationId)
    {
        if (!ShouldCaptureDbQueryText(command, instrumentationId))
            return;

        activity.SetTag(QylSemanticAttributes.DbQueryText, command.CommandText);
    }

    public static void SetGraphQlDocument(Activity activity, string? document)
    {
        if (!QylAutoInstrumentationOptions.Current.GraphQlSetDocument ||
            string.IsNullOrWhiteSpace(document))
        {
            return;
        }

        activity.SetTag(QylSemanticAttributes.GraphQlDocument, document);
    }

    private static bool ShouldCaptureDbQueryText(DbCommand command, string instrumentationId)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        var options = QylAutoInstrumentationOptions.Current;
        return instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => options.SqlClientSetDbStatementForText,
            QylAutoInstrumentationIds.EntityFrameworkCore => options.EntityFrameworkCoreSetDbStatementForText,
            QylAutoInstrumentationIds.OracleMda => options.OracleMdaSetDbStatementForText,
            _ => false,
        };
    }
}
