using System.Diagnostics;
using OpenTelemetry;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

namespace Qyl;

/// <summary>
/// One row of the native-source table: the <c>ActivitySource</c> qyl subscribes to, the
/// instrumentation id whose toggle gates it, the <c>qyl.instrumentation.domain</c> value stamped on
/// its spans, and the optional normalisation that row needs.
/// </summary>
/// <remarks>
/// A <see cref="SourceName"/> ending in <c>*</c> matches by prefix, which is what the Azure SDK's
/// family of per-service sources needs; every other row is an ordinal exact match.
/// </remarks>
internal readonly record struct QylNativeSourceRow(
    string SourceName,
    string InstrumentationId,
    string? Domain,
    Action<Activity>? Normalize)
{
    internal bool Matches(string sourceName)
        => SourceName.EndsWith('*')
            ? sourceName.AsSpan().StartsWith(SourceName.AsSpan(0, SourceName.Length - 1), StringComparison.Ordinal)
            : StringComparer.Ordinal.Equals(sourceName, SourceName);
}

/// <summary>
/// Stamps the qyl attributes onto the spans the libraries emit themselves, driven by the
/// native-source table rather than by one processor per library.
/// </summary>
internal sealed class QylNativeSpanProcessor(QylNativeSourceRow[] rows) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        foreach (var row in rows)
        {
            if (!row.Matches(data.Source.Name))
                continue;

            if (row.Domain is { } domain)
                data.SetTag(QylAttributes.InstrumentationDomain, domain);

            row.Normalize?.Invoke(data);
            return;
        }
    }

    /// <summary>
    /// The Azure SDK spans carry the full request URL and the assembly-qualified exception type;
    /// qyl drops the URL and reports the short type name.
    /// </summary>
    internal static void NormalizeAzure(Activity data)
    {
        data.SetTag(UrlAttributes.Full, null);
        data.SetTag(UrlAttributes.Path, null);

        if (data.Status is not ActivityStatusCode.Error)
            return;

        var exceptionType = data.GetTagItem(ErrorAttributes.Type) as string ?? FindExceptionType(data);
        if (exceptionType is not null)
            data.SetTag(ErrorAttributes.Type, GetSimpleTypeName(exceptionType));
    }

    /// <summary>CoreWCF still reports the pre-stable <c>rpc.system</c> key; qyl reports the stable one.</summary>
    internal static void NormalizeCoreWcf(Activity data)
    {
        const string legacyRpcSystem = "rpc.system";

        if (data.GetTagItem(legacyRpcSystem) is { } system)
            data.SetTag(RpcAttributes.SystemName, system);

        data.SetTag(legacyRpcSystem, null);
    }

    private static string? FindExceptionType(Activity activity)
    {
        foreach (var activityEvent in activity.Events)
        {
            foreach (var tag in activityEvent.Tags)
            {
                if (StringComparer.Ordinal.Equals(tag.Key, "exception.type") && tag.Value is string exceptionType)
                    return exceptionType;
            }
        }

        return null;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator >= 0 ? typeName[(separator + 1)..] : typeName;
    }
}
