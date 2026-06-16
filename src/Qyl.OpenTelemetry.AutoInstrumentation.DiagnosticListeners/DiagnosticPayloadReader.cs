using System.Diagnostics;
using System.Globalization;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners;

internal static class DiagnosticPayloadReader
{
    public static string? GetString(object? payload, string key)
    {
        if (TryGetPayloadValue(payload, key, out var value) && value is not null)
            return Convert.ToString(value, CultureInfo.InvariantCulture);

        var currentValue = Activity.Current?.GetTagItem(key);
        return currentValue is null ? null : Convert.ToString(currentValue, CultureInfo.InvariantCulture);
    }

    public static string? GetString(object? payload, string key, string alias)
        => GetString(payload, key) ?? GetString(payload, alias);

    public static int? GetInt32(object? payload, string key)
    {
        if (!TryGetPayloadValue(payload, key, out var value) || value is null)
            value = Activity.Current?.GetTagItem(key);

        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            short shortValue => shortValue,
            string stringValue when int.TryParse(
                stringValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    public static int? GetInt32(object? payload, string key, string alias)
        => GetInt32(payload, key) ?? GetInt32(payload, alias);

    private static bool TryGetPayloadValue(object? payload, string key, out object? value)
    {
        if (payload is IReadOnlyDictionary<string, object?> readOnlyDictionary &&
            readOnlyDictionary.TryGetValue(key, out value))
        {
            return true;
        }

        if (payload is IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
        {
            foreach (var pair in keyValuePairs)
            {
                if (StringComparer.Ordinal.Equals(pair.Key, key))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
