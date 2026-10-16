using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylCaptureHelpers
{
    public static void SetRequestHeaders(
        Activity? activity,
        QylCapturedNameMap configuredHeaders,
        IHeaderDictionary headers)
    {
        if (activity is null || configuredHeaders.Count is 0)
            return;

        for (var index = 0; index < configuredHeaders.Count; index++)
        {
            var lookupName = configuredHeaders.GetLookupName(index);
            if (headers.TryGetValue(lookupName, out var values) && values.Count > 0)
                activity.SetTag(configuredHeaders.GetTagName(index), ToTagValues(values));
        }
    }

    public static string RedactQueryValues(string query)
    {
        if (query.IndexOf('=', StringComparison.Ordinal) < 0)
            return query;

        var builder = new StringBuilder(2 * query.Length);
        for (var index = 0; index < query.Length; index++)
        {
            var current = query[index];
            if (current is '=')
            {
                builder.Append("=Redacted");
                index++;
                while (index < query.Length && query[index] is not '&')
                    index++;
                if (index < query.Length)
                    builder.Append('&');
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string[] ToTagValues(StringValues values)
        => values.Count is 0
            ? []
            : [.. values.Where(static value => value is not null).Select(static value => value!)];

}
