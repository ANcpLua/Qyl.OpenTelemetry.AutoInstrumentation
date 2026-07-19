using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qyl.OpenTelemetry.AutoInstrumentation;
using StackExchange.Redis;

var configuration = Environment.GetEnvironmentVariable("QYL_REDIS_CONFIGURATION");
if (string.IsNullOrWhiteSpace(configuration))
{
    Console.Error.WriteLine("QYL_REDIS_CONFIGURATION is required.");
    return 2;
}

var captured = new List<CapturedActivity>();
var capturedLock = new Lock();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            captured.Add(CapturedActivity.From(activity));
        }
    },
};

ActivitySource.AddActivityListener(listener);

await using (var connection = await WaitForRedisAsync(configuration))
{
    var database = connection.GetDatabase();

    var stored = await database.StringSetAsync("qyl:probe", "alpha");
    Console.WriteLine("stored=" + stored.ToString(CultureInfo.InvariantCulture));

    var value = await database.StringGetAsync("qyl:probe");
    Console.WriteLine("value=" + value);

    var deleted = await database.KeyDeleteAsync("qyl:probe");
    Console.WriteLine("deleted=" + deleted.ToString(CultureInfo.InvariantCulture));

    try
    {
        _ = await database.ExecuteAsync("QYLNOSUCH");
    }
    catch (RedisServerException exception)
    {
        Console.WriteLine("expected-redis-error=" + exception.GetType().Name);
    }
}

var report = RedisReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealRedisJsonContext.Default.RedisReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task<ConnectionMultiplexer> WaitForRedisAsync(string configuration)
{
    Exception? lastException = null;

    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            return await ConnectionMultiplexer.ConnectAsync(configuration + ",connectTimeout=2000,abortConnect=true");
        }
        catch (RedisConnectionException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("Redis did not become ready.", lastException);
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record RedisReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static RedisReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var redisSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.redis"))
            .ToArray();

        if (redisSpans.Length != 4)
            failures.Add($"expected 4 Redis command spans, got {redisSpans.Length}");

        var set = FindByOperationAndStatus(redisSpans, "SET", "Unset");
        var get = FindByOperationAndStatus(redisSpans, "GET", "Unset");
        var del = FindByOperationAndStatus(redisSpans, "DEL", "Unset");
        var executeError = FindByOperationAndStatus(redisSpans, "EXECUTE", "Error");

        Require(set, "successful SET span", failures);
        Require(get, "successful GET span", failures);
        Require(del, "successful DEL span", failures);
        Require(executeError, "error EXECUTE span", failures);
        RequireTag(executeError, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(StackExchange.Redis.RedisServerException).FullName!, failures);

        foreach (var span in redisSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "Redis command"))
                failures.Add($"unexpected Redis span name: {span.Name}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.Redis, failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");
        }

        return new RedisReport(runtimeMode, failures.Count is 0, failures.ToArray(), redisSpans);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(RedisReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealRedisJsonContext : JsonSerializerContext;
