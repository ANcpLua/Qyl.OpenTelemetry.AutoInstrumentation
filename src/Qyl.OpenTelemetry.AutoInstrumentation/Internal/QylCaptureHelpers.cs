using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylCaptureHelpers
{
    public static void SetHttpHeaders(
        Activity? activity,
        QylCapturedNameMap configuredHeaders,
        HttpHeaders? primaryHeaders,
        HttpHeaders? contentHeaders)
        => SetHttpHeaders(activity, configuredHeaders, primaryHeaders, null, contentHeaders);

    public static void SetHttpHeaders(
        Activity? activity,
        QylCapturedNameMap configuredHeaders,
        HttpHeaders? primaryHeaders,
        HttpHeaders? fallbackHeaders,
        HttpHeaders? contentHeaders)
    {
        if (activity is null || configuredHeaders.Count is 0)
            return;

        for (var index = 0; index < configuredHeaders.Count; index++)
        {
            var lookupName = configuredHeaders.GetLookupName(index);

            if (TrySetHeaderTag(activity, configuredHeaders, index, lookupName, primaryHeaders))
                continue;

            if (TrySetHeaderTag(activity, configuredHeaders, index, lookupName, fallbackHeaders))
                continue;

            TrySetHeaderTag(activity, configuredHeaders, index, lookupName, contentHeaders);
        }
    }

    public static void SetMetadataHeaders(
        Activity? activity,
        QylCapturedNameMap configuredMetadata,
        Metadata? metadata)
    {
        if (activity is null || metadata is null || configuredMetadata.Count is 0)
            return;

        for (var index = 0; index < configuredMetadata.Count; index++)
        {
            var lookupName = configuredMetadata.GetLookupName(index);
            List<string>? values = null;
            foreach (var entry in metadata)
            {
                if (entry.IsBinary || !string.Equals(entry.Key, lookupName, StringComparison.OrdinalIgnoreCase))
                    continue;

                (values ??= []).Add(entry.Value);
            }

            if (values is { Count: > 0 })
                activity.SetTag(configuredMetadata.GetTagName(index), values.ToArray());
        }
    }

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

    public static string RedactQuery(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return url;

        var fragmentStart = url.IndexOf('#', queryStart);
        var queryEnd = fragmentStart < 0 ? url.Length : fragmentStart;
        var redacted = RedactQueryValues(url[(queryStart + 1)..queryEnd]);
        return fragmentStart < 0
            ? url[..(queryStart + 1)] + redacted
            : url[..(queryStart + 1)] + redacted + url[fragmentStart..];
    }

    public static string RedactQueryValues(string query)
    {
        if (query.IndexOf('=', StringComparison.Ordinal) < 0)
            return query;

        var builder = new StringBuilder(2 * query.Length);
        var index = 0;
        while (index < query.Length)
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

            index++;
        }

        return builder.ToString();
    }

    public static string FormatUrlFull(string url, bool queryRedactionDisabled)
        => queryRedactionDisabled ? url : RedactQuery(url);

    private static string[] ToTagValues(StringValues values)
        => values.Count is 0
            ? []
            : values.Where(static value => value is not null).Select(static value => value!).ToArray();

    private static string[] ToTagValues(IEnumerable<string> values)
        => values as string[] ?? values.ToArray();

    private static bool TrySetHeaderTag(
        Activity activity,
        QylCapturedNameMap configuredHeaders,
        int index,
        string lookupName,
        HttpHeaders? headers)
    {
        if (headers is null || !headers.TryGetValues(lookupName, out var values))
            return false;

        activity.SetTag(configuredHeaders.GetTagName(index), ToTagValues(values));
        return true;
    }
}
