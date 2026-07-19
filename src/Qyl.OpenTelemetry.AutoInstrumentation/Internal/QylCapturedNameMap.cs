using System.Globalization;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal sealed class QylCapturedNameMap
{
    internal static readonly QylCapturedNameMap Empty = new([], []);

    private readonly string[] _lookupNames;
    private readonly string[] _tagNames;

    private QylCapturedNameMap(string[] lookupNames, string[] tagNames)
    {
        _lookupNames = lookupNames;
        _tagNames = tagNames;
    }

    internal int Count => _lookupNames.Length;

    internal string GetLookupName(int index) => _lookupNames[index];

    internal string GetTagName(int index) => _tagNames[index];

    internal static QylCapturedNameMap Create(string prefix, string[] configuredNames)
    {
        if (configuredNames.Length is 0)
            return Empty;

        var entries = new Dictionary<string, string>(configuredNames.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var configuredName in configuredNames)
        {
            var trimmedName = configuredName.Trim();
            if (trimmedName.Length is 0)
                continue;

            var normalizedName = NormalizeName(trimmedName);
            entries[trimmedName] = prefix + normalizedName;
        }

        if (entries.Count is 0)
            return Empty;

        var lookupNames = new string[entries.Count];
        var tagNames = new string[entries.Count];
        var index = 0;
        foreach (var entry in entries)
        {
            lookupNames[index] = entry.Key;
            tagNames[index] = entry.Value;
            index++;
        }

        return new QylCapturedNameMap(lookupNames, tagNames);
    }

    private static string NormalizeName(string name)
        => name.Replace('_', '-').ToLower(CultureInfo.InvariantCulture);
}
