using System.Data.Common;
using System.Diagnostics;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using GraphqlAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes;
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

    public static void SetHttpClientUrlFull(Activity activity, string url)
    {
        activity.SetTag(
            UrlAttributes.Full,
            QylCaptureHelpers.FormatUrlFull(
                url,
                QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled));
    }

    public static void SetDbQueryText(Activity activity, DbCommand command, string instrumentationId)
    {
        if (!ShouldCaptureDbQueryText(command, instrumentationId))
            return;

        activity.SetTag(DbAttributes.QueryText, command.CommandText);
    }

    public static void SetGraphQlDocument(Activity activity, string? document)
    {
        if (!QylAutoInstrumentationOptions.Current.GraphQlSetDocument ||
            string.IsNullOrWhiteSpace(document))
        {
            return;
        }

        activity.SetTag(GraphqlAttributes.Document, document);
    }

    private static bool ShouldCaptureDbQueryText(DbCommand command, string instrumentationId)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        var options = QylAutoInstrumentationOptions.Current;
        return instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => options.SqlClientSetDbStatementForText,
            QylAutoInstrumentationIds.OracleMda => options.OracleMdaSetDbStatementForText,
            _ => false,
        };
    }
}
