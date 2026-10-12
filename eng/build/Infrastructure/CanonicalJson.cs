using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Qyl.Build;

/// <summary>
/// The canonical text of a JSON document for a committed verified file: object keys in ordinal order,
/// two-space indentation, ", "/": " separators, non-ASCII escaped as \uXXXX and a final newline: the
/// exact bytes of the committed verified files, so they compare equal byte for byte.
/// </summary>
static class CanonicalJson
{
    public static string Render(JsonElement element)
    {
        var builder = new StringBuilder();
        Write(builder, element, 0);
        return builder.Append('\n').ToString();
    }

    public static string Render(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Render(document.RootElement);
    }

    private static void Write(StringBuilder builder, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Length == 0)
                {
                    builder.Append("{}");
                    return;
                }

                builder.Append("{\n");
                for (var index = 0; index < properties.Length; index++)
                {
                    Indent(builder, depth + 1);
                    WriteString(builder, properties[index].Name);
                    builder.Append(": ");
                    Write(builder, properties[index].Value, depth + 1);
                    builder.Append(index < properties.Length - 1 ? ",\n" : "\n");
                }

                Indent(builder, depth);
                builder.Append('}');
                return;
            case JsonValueKind.Array:
                var items = element.EnumerateArray().ToArray();
                if (items.Length == 0)
                {
                    builder.Append("[]");
                    return;
                }

                builder.Append("[\n");
                for (var index = 0; index < items.Length; index++)
                {
                    Indent(builder, depth + 1);
                    Write(builder, items[index], depth + 1);
                    builder.Append(index < items.Length - 1 ? ",\n" : "\n");
                }

                Indent(builder, depth);
                builder.Append(']');
                return;
            case JsonValueKind.String:
                WriteString(builder, element.GetString()!);
                return;
            case JsonValueKind.Number:
                builder.Append(element.TryGetInt64(out var integer)
                    ? integer.ToString(CultureInfo.InvariantCulture)
                    : FormatDouble(element.GetDouble()));
                return;
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            default:
                builder.Append("null");
                return;
        }
    }

    // A float keeps ".0" when integral and writes its exponent lower-case with a sign.
    private static string FormatDouble(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('E', StringComparison.Ordinal))
            return text.Replace("E", "e", StringComparison.Ordinal);
        return text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
    }

    private static void Indent(StringBuilder builder, int depth) => builder.Append(' ', depth * 2);

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (character < 0x20 || character > 0x7E)
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }
}
