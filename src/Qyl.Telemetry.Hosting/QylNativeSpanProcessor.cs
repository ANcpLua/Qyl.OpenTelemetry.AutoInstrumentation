using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpenTelemetry;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using GenAiAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.GenAi.GenAiAttributes;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;

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
/// Applies the native-source domain and explicit legacy wire-type conversions before export.
/// </summary>
/// <remarks>
/// Known legacy representations are converted without changing their meaning: the AI SDK's JSON
/// finish-reason array and MassTransit's numeric message-size string. Other attributes remain
/// intact, including invalid values that cannot be converted. A deprecated key such as
/// CoreWCF's <c>rpc.system</c> is rewritten by the collector's
/// <c>AttributeMapping.TryGetRename</c>, and vendor keys such as
/// <c>elastic.transport.product.name</c> reach it unchanged as pass-through tags.
/// </remarks>
internal sealed class QylNativeSpanProcessor(QylNativeSourceRow[] rows) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (data.Source.Name is QylTelemetrySources.MicrosoftExtensionsAi or QylTelemetrySources.MicrosoftAgentsAi &&
            data.GetTagItem(GenAiAttributes.ResponseFinishReasons) is string reasons)
            NormalizeFinishReasons(data, reasons);

        if (data.Source.Name == QylTelemetrySources.MassTransit &&
            data.GetTagItem(MessagingAttributes.MessageBodySize) is string size &&
            long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0)
            data.SetTag(MessagingAttributes.MessageBodySize, bytes);

        foreach (var row in rows)
        {
            if (!row.Matches(data.Source.Name))
                continue;

            if (row.Domain is { } domain)
                data.SetTag(QylAttributes.InstrumentationDomain, domain);

            return;
        }
    }

    private static void NormalizeFinishReasons(Activity activity, string value)
    {
        // The pinned official SDK emits a JSON string where the convention requires string[].
        // https://github.com/dotnet/extensions/blob/25f07afa51034e37b334210642d7dc5f191c142d/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClient.cs#L447
        // Only a complete string array is converted; malformed or differently typed content stays visible.
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.EnumerateArray().Any(static element => element.ValueKind != JsonValueKind.String))
                return;
            activity.SetTag(GenAiAttributes.ResponseFinishReasons,
                document.RootElement.EnumerateArray().Select(static element => element.GetString()!).ToArray());
        }
        catch (JsonException)
        {
            // Preserve the original value so registry diagnostics can report the producer's invalid type.
        }
    }
}
