using System.Globalization;

namespace Qyl.Build;

/// <summary>
/// Values of the contract YAML object graph (see <see cref="Yaml"/>): their text, how a message
/// renders them, integer reads, keyed reads and deep equality, the operations the contract rules
/// are written in.
/// </summary>
static class ContractValue
{
    public static string Text(object? value) => value switch
    {
        null => "null",
        string text => text,
        _ => Render(value),
    };

    public static string Render(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'") + "'",
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable<KeyValuePair<string, object?>> mapping => "{" + string.Join(", ", mapping.Select(static pair => Render(pair.Key) + ": " + Render(pair.Value))) + "}",
        System.Collections.IEnumerable sequence => "[" + string.Join(", ", sequence.Cast<object?>().Select(Render)) + "]",
        _ => value.ToString() ?? string.Empty,
    };

    public static long Int(object? value) => value switch
    {
        long number => number,
        int number => number,
        double number => (long)number,
        bool flag => flag ? 1 : 0,
        string text when long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw Gate.Fail($"not an integer: {Render(value)}"),
    };

    /// <summary>The value under <paramref name="key"/>, or null when the key is absent.</summary>
    public static object? Get(this OrderedDictionary<string, object?> mapping, string key) =>
        mapping.TryGetValue(key, out var value) ? value : null;

    /// <summary>The text of the value under <paramref name="key"/>, or <paramref name="fallback"/> when the key is absent.</summary>
    public static string GetText(this OrderedDictionary<string, object?> mapping, string key, string fallback = "") =>
        mapping.TryGetValue(key, out var value) ? Text(value) : fallback;

    public static string RenderSorted(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.OrderBy(static value => value, StringComparer.Ordinal).Select(static value => Render(value))) + "]";

    public static IEnumerable<string> Sorted(this IEnumerable<string> values) => values.OrderBy(static value => value, StringComparer.Ordinal);
}
