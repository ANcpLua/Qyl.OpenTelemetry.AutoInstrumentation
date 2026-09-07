using System.Diagnostics;
using OpenTelemetry;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl;

/// <summary>
/// One row of the native-source table: the <c>ActivitySource</c> qyl subscribes to, the
/// instrumentation id whose toggle gates it, and the <c>qyl.instrumentation.domain</c> value
/// stamped on its spans.
/// </summary>
/// <remarks>
/// A <see cref="SourceName"/> ending in <c>*</c> matches by prefix, which is what the Azure SDK's
/// family of per-service sources needs; every other row is an ordinal exact match.
/// </remarks>
internal readonly record struct QylNativeSourceRow(
    string SourceName,
    string InstrumentationId,
    string? Domain)
{
    internal bool Matches(string sourceName)
        => SourceName.EndsWith('*')
            ? sourceName.AsSpan().StartsWith(SourceName.AsSpan(0, SourceName.Length - 1), StringComparison.Ordinal)
            : StringComparer.Ordinal.Equals(sourceName, SourceName);
}

/// <summary>
/// Stamps <c>qyl.instrumentation.domain</c> onto the spans the libraries emit themselves, driven
/// by the native-source table rather than by one processor per library.
/// </summary>
/// <remarks>
/// That attribute is the whole contract: the instrumentation writes only keys the registry
/// defines and never rewrites, drops or renames what a library emitted. A deprecated key such as
/// CoreWCF's <c>rpc.system</c> is rewritten by the collector's
/// <c>AttributeMapping.TryGetRename</c>, and vendor keys such as
/// <c>elastic.transport.product.name</c> reach it unchanged as pass-through tags.
/// </remarks>
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

            return;
        }
    }
}
