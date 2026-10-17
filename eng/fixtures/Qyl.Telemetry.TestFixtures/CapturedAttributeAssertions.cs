using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Qyl.Telemetry.TestFixtures;

/// <summary>Checks the raw captured values exported by native fixture applications.</summary>
public static class CapturedAttributeAssertions
{
    /// <summary>Requires an exact string array, including its order, duplicates and empty values.</summary>
    public static void RequireValues(IReadOnlyDictionary<string, object?> attributes, string key, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);

        var present = attributes.TryGetValue(key, out var value);
        if (present && value is string[] actual && actual.SequenceEqual(expected, StringComparer.Ordinal))
            return;

        var observed = present ? Describe(value) : "<missing>";
        throw new InvalidOperationException(
            $"Captured attribute '{key}': expected string[] {FormatValues(expected)}; actual {observed}.");
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"String: {FormatValue(text)}",
        IEnumerable values => $"{value.GetType().Name}: {FormatValues(values)}",
        _ => $"{value.GetType().Name}: {FormatValue(value)}"
    };

    private static string FormatValues(IEnumerable values) =>
        $"[{string.Join(", ", values.Cast<object?>().Select(FormatValue))}]";

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{JsonEncodedText.Encode(text)}\"",
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}
