using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Qyl.Build;

/// <summary>
/// The contract YAML as a plain object graph: <see cref="OrderedDictionary{TKey,TValue}"/> for a
/// mapping (document order kept), <see cref="List{T}"/> for a sequence, and string, long, double,
/// bool or null for a scalar. Plain scalars resolve by the YAML 1.1 rules the contract files were
/// written against (yes/no/on/off are booleans, 0x/0o ints); quoted scalars are always strings.
/// </summary>
static partial class Yaml
{
    public static object? Load(string path)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(path, Encoding.UTF8))
            stream.Load(reader);
        return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode);
    }

    public static OrderedDictionary<string, object?> LoadMapping(string path) =>
        Load(path) as OrderedDictionary<string, object?> ?? throw Gate.Fail($"YAML root must be an object: {Repo.Relative((Fallout.Common.IO.AbsolutePath)path)}");

    private static object? Convert(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ConvertMapping(mapping),
        YamlSequenceNode sequence => sequence.Children.Select(Convert).ToList(),
        YamlScalarNode scalar => Resolve(scalar),
        _ => throw Gate.Fail($"unsupported YAML node at {node.Start}"),
    };

    private static OrderedDictionary<string, object?> ConvertMapping(YamlMappingNode mapping)
    {
        var result = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in mapping.Children)
        {
            var name = key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : throw Gate.Fail($"non-scalar YAML key at {key.Start}");
            Gate.Require(!result.ContainsKey(name), $"duplicate YAML key {name} at {key.Start}");
            result[name] = Convert(value);
        }

        return result;
    }

    private static object? Resolve(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style is not ScalarStyle.Plain)
            return value;

        if (value is "" or "~" or "null" or "Null" or "NULL")
            return null;
        if (value is "true" or "True" or "TRUE" or "yes" or "Yes" or "YES" or "on" or "On" or "ON")
            return true;
        if (value is "false" or "False" or "FALSE" or "no" or "No" or "NO" or "off" or "Off" or "OFF")
            return false;

        if (DecimalInt().IsMatch(value))
            return long.Parse(value.Replace("_", string.Empty), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (HexInt().Match(value) is { Success: true } hex)
            return ApplySign(hex.Groups[1].Value, System.Convert.ToInt64(hex.Groups[2].Value.Replace("_", string.Empty), 16));
        if (OctalInt().Match(value) is { Success: true } octal)
            return ApplySign(octal.Groups[1].Value, System.Convert.ToInt64(octal.Groups[2].Value.Replace("_", string.Empty), 8));
        if (Float().IsMatch(value))
            return double.Parse(value.Replace("_", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture);

        return value;
    }

    private static long ApplySign(string sign, long value) => sign == "-" ? -value : value;

    /// <summary>Block-style YAML for a generated artifact; strings that would read back as another type are quoted.</summary>
    public static string Dump(object? graph)
    {
        var serializer = new SerializerBuilder()
            .WithQuotingNecessaryStrings()
            .DisableAliases()
            .Build();
        return serializer.Serialize(graph);
    }

    /// <summary>Two-space indented JSON with no escaping beyond what JSON requires.</summary>
    public static string ToJson(object? graph)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteJson(writer, graph);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteJson(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case IEnumerable<KeyValuePair<string, object?>> mapping:
                writer.WriteStartObject();
                foreach (var (key, item) in mapping)
                {
                    writer.WritePropertyName(key);
                    WriteJson(writer, item);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                    WriteJson(writer, item);
                writer.WriteEndArray();
                break;
            default: throw Gate.Fail($"cannot write {value.GetType()} as JSON");
        }
    }

    [GeneratedRegex(@"^[-+]?(?:0|[1-9][0-9_]*)$")]
    private static partial Regex DecimalInt();

    [GeneratedRegex(@"^([-+]?)0x([0-9a-fA-F_]+)$")]
    private static partial Regex HexInt();

    [GeneratedRegex(@"^([-+]?)0o?([0-7_]+)$")]
    private static partial Regex OctalInt();

    [GeneratedRegex(@"^(?:[-+]?[0-9][0-9_]*\.[0-9_]*(?:[eE][-+][0-9]+)?|[-+]?\.[0-9_]+(?:[eE][-+][0-9]+)?)$")]
    private static partial Regex Float();
}
