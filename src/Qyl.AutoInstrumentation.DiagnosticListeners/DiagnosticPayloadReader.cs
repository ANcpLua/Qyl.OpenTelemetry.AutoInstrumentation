using System.Diagnostics;
using System.Globalization;

namespace Qyl.AutoInstrumentation.DiagnosticListeners;

internal static class DiagnosticPayloadReader
{
    public static string GetString(object? payload, string key, string fallback)
    {
        if (TryGetPayloadValue(payload, key, out var value) && value is not null)
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;

        var currentValue = Activity.Current?.GetTagItem(key);
        return currentValue is null
            ? fallback
            : Convert.ToString(currentValue, CultureInfo.InvariantCulture) ?? fallback;
    }

    public static int GetInt32(object? payload, string key, int fallback)
    {
        if (!TryGetPayloadValue(payload, key, out var value) || value is null)
            value = Activity.Current?.GetTagItem(key);

        return value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            string stringValue when int.TryParse(
                stringValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => fallback,
        };
    }

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
