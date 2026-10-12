using System.Text.Json;

namespace Qyl.Build;

enum LaneRuntime
{
    Managed,
    NativeAot,
}

/// <summary>
/// One execution of a demo: its process result, the report it printed, and the time window its
/// telemetry was exported in. The assertions restate what the demo already asserts in-process, so a
/// demo that quietly relaxes its own expectation still fails its lane.
/// </summary>
sealed class DemoRun
{
    private JsonElement? _report;

    public DemoRun(string label, LaneRuntime runtime, ProcessResult process, DateTimeOffset started, DateTimeOffset finished)
    {
        Label = label;
        Runtime = runtime;
        Process = process;
        Started = started;
        Finished = finished;
    }

    public string Label { get; }

    public LaneRuntime Runtime { get; }

    public ProcessResult Process { get; }

    public DateTimeOffset Started { get; }

    public DateTimeOffset Finished { get; }

    public string Stdout => Process.Stdout;

    public string ExpectedRuntimeMode => Runtime is LaneRuntime.NativeAot ? "nativeaot" : "dynamic-code-supported";

    /// <summary>Exit code 0 and nothing on stderr.</summary>
    public DemoRun RequireSucceeded()
    {
        Gate.Require(Process.ExitCode == 0, () => $"{Label} failed\nexit={Process.ExitCode}\nstdout={Process.Stdout}\nstderr={Process.Stderr}");
        Gate.Require(Process.Stderr.Length == 0, () => $"{Label} wrote stderr:\n{Process.Stderr}");
        return this;
    }

    public DemoRun RequireTokens(params string[] tokens)
    {
        foreach (var token in tokens)
            Gate.Require(Process.Stdout.Contains(token, StringComparison.Ordinal), () => $"{Label} missing output token '{token}'\nstdout={Process.Stdout}");
        return this;
    }

    public DemoRun RequireTokenCount(string token, int count)
    {
        var actual = Count(Process.Stdout, token);
        Gate.Require(actual == count, () => $"{Label} expected exactly {count} '{token}' tokens, found {actual}\nstdout={Process.Stdout}");
        return this;
    }

    /// <summary>The JSON report the demo prints last, from its first <c>{\n</c> to the end.</summary>
    public JsonElement Report
    {
        get
        {
            if (_report is { } cached)
                return cached;
            var start = Process.Stdout.IndexOf("{\n", StringComparison.Ordinal);
            Gate.Require(start >= 0, () => $"{Label} did not emit a JSON report\nstdout={Process.Stdout}");
            try
            {
                using var document = JsonDocument.Parse(Process.Stdout[start..]);
                Gate.Require(document.RootElement.ValueKind is JsonValueKind.Object, $"{Label} report must be a JSON object");
                _report = document.RootElement.Clone();
                return _report.Value;
            }
            catch (JsonException exception)
            {
                throw Gate.Fail($"{Label} emitted an invalid JSON report: {exception.Message}\nstdout={Process.Stdout}");
            }
        }
    }

    public string Dump() => JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>RuntimeMode matches the runtime this run used, and the demo's own verdict is Pass.</summary>
    public DemoRun RequireReportPassed()
    {
        Gate.Require(StringField("RuntimeMode") == ExpectedRuntimeMode, () => $"{Label} runtime mode mismatch: expected={ExpectedRuntimeMode} actual={StringField("RuntimeMode")}");
        return RequirePass();
    }

    public DemoRun RequirePass()
    {
        Gate.Require(Has("Pass") && Report.GetProperty("Pass").ValueKind is JsonValueKind.True, () => $"{Label} report did not pass:\n{Dump()}");
        return this;
    }

    public bool Has(string field) => Report.TryGetProperty(field, out _);

    public JsonElement Field(string field)
    {
        Gate.Require(Report.TryGetProperty(field, out var value), () => $"{Label} report has no {field}:\n{Dump()}");
        return value;
    }

    public string? StringField(string field) =>
        Report.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    public bool? BoolField(string field) =>
        Report.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    public DemoRun RequireBool(string field, bool expected)
    {
        Gate.Require(BoolField(field) == expected, () => $"{Label} expected {field}={expected}, got {BoolField(field)?.ToString() ?? "absent"}:\n{Dump()}");
        return this;
    }

    public JsonElement Array(string field)
    {
        var value = Field(field);
        Gate.Require(value.ValueKind is JsonValueKind.Array, () => $"{Label} report {field} must be an array: {value}");
        return value;
    }

    public JsonElement RequireCount(string field, int expected, string what)
    {
        var array = Array(field);
        Gate.Require(array.GetArrayLength() == expected, () => $"{Label} expected exactly {expected} {what}, got {array.GetArrayLength()}:\n{array}");
        return array;
    }

    public JsonElement RequireAtLeast(string field, int expected, string what)
    {
        var array = Array(field);
        Gate.Require(array.GetArrayLength() >= expected, () => $"{Label} expected at least {expected} {what}, got {array.GetArrayLength()}:\n{array}");
        return array;
    }

    public static IReadOnlyList<string?> Strings(JsonElement array) =>
        array.EnumerateArray().Select(static item => item.ValueKind is JsonValueKind.String ? item.GetString() : null).ToArray();

    private static int Count(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}

/// <summary>Reads a reported activity's fields whichever of the demos' record shapes it has.</summary>
static class ReportedActivity
{
    public static string? Text(JsonElement activity, string field) =>
        activity.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    /// <summary>A tag's text: a plain string, or the <c>Value</c> of a typed tag record.</summary>
    public static string? Tag(JsonElement activity, string key)
    {
        if (!activity.TryGetProperty("Tags", out var tags) || tags.ValueKind is not JsonValueKind.Object || !tags.TryGetProperty(key, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("Value", out var inner) => inner.ValueKind is JsonValueKind.String ? inner.GetString() : inner.ToString(),
            JsonValueKind.Null => null,
            _ => value.ToString(),
        };
    }

    public static bool HasTag(JsonElement activity, string key) =>
        activity.TryGetProperty("Tags", out var tags) && tags.ValueKind is JsonValueKind.Object && tags.TryGetProperty(key, out _);

    public static IEnumerable<string> TagKeys(JsonElement activity) =>
        activity.TryGetProperty("Tags", out var tags) && tags.ValueKind is JsonValueKind.Object
            ? tags.EnumerateObject().Select(static property => property.Name)
            : [];
}
