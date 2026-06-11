using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Qyl.AutoInstrumentation.Internal;

internal static class QylCaptureHelpers
{
    public static void SetHttpHeaders(
        Activity? activity,
        QylCapturedNameMap configuredHeaders,
        params HttpHeaders?[] headerSources)
    {
        if (activity is null || configuredHeaders.Count is 0)
            return;

        for (var index = 0; index < configuredHeaders.Count; index++)
        {
            var lookupName = configuredHeaders.GetLookupName(index);
            foreach (var source in headerSources)
            {
                if (source is null || !source.TryGetValues(lookupName, out var values))
                    continue;

                activity.SetTag(configuredHeaders.GetTagName(index), ToTagValues(values));
                break;
            }
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

    public static void SetRequestHeaders(
        Activity? activity,
        QylCapturedNameMap configuredHeaders,
        WebHeaderCollection headers)
    {
        if (activity is null || configuredHeaders.Count is 0)
            return;

        for (var index = 0; index < configuredHeaders.Count; index++)
        {
            var values = headers.GetValues(configuredHeaders.GetLookupName(index));
            if (values is { Length: > 0 })
                activity.SetTag(configuredHeaders.GetTagName(index), values);
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

    public static string RedactQuery(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return url;

        var fragmentStart = url.IndexOf('#', queryStart);
        return fragmentStart < 0
            ? url[..queryStart] + "?Redacted"
            : url[..queryStart] + "?Redacted" + url[fragmentStart..];
    }

    public static string TrimQueryPrefix(string? query)
        => string.IsNullOrEmpty(query) || query[0] is not '?'
            ? query ?? string.Empty
            : query[1..];

    private static string[] ToTagValues(StringValues values)
        => values.Count is 0
            ? []
            : values.Where(static value => value is not null).Select(static value => value!).ToArray();

    private static string[] ToTagValues(IEnumerable<string> values)
        => values as string[] ?? values.ToArray();
}
