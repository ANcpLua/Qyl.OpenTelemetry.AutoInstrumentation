using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();

using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddHealthChecks();
var app = builder.Build();

app.MapHealthChecks("/healthz");
app.MapGet("/items/{id:int}", (HttpContext context) =>
{
    context.Response.Headers["X-Demo-Res"] = "sv1";
    context.Response.StatusCode = StatusCodes.Status204NoContent;
    return Task.CompletedTask;
});
app.MapGet("/fail/{id:int}", (HttpContext _) => throw new InvalidOperationException("expected route failure"));

await app.StartAsync();

try
{
    var address = app.Urls.Single();
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("X-Demo-Req", "rv1");
    using (await httpClient.GetAsync($"{address}/items/42?sample=1"))
    {
    }

    using (await httpClient.GetAsync($"{address}/fail/13?sample=1"))
    {
    }
}
finally
{
    await app.StopAsync();
}

var report = AspNetCoreReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealAspNetCoreJsonContext.Default.AspNetCoreReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

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
                static tag => tag.Value switch
                {
                    string s => s,
                    System.Collections.IEnumerable e => string.Join(",", e.Cast<object?>()),
                    var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                },
                StringComparer.Ordinal));
}

internal sealed record AspNetCoreReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static AspNetCoreReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var httpServerSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "http.server"))
            .ToArray();

        if (httpServerSpans.Length != 2)
            failures.Add($"expected 2 real ASP.NET Core server spans, got {httpServerSpans.Length}");

        var successSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue("http.response.status_code", out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "204"));
        var failureSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue("http.response.status_code", out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "500"));

        Require(successSpan, "204 route span", failures);
        Require(failureSpan, "500 route span", failures);
        RequireTag(successSpan, "http.request.method", "GET", failures);
        RequireTag(successSpan, "http.route", "/items/{id:int}", failures);
        RequireTag(failureSpan, "http.route", "/fail/{id:int}", failures);
        RequireTag(failureSpan, "error.type", "500", failures);
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Error", failures);

        // Option rows are asserted in both directions, keyed off the same env vars
        // the runtime honors: header capture opt-in and URL query redaction.
        var captureOptIn = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"));
        var redactionDisabled = string.Equals(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        RequireTag(successSpan, "url.query", redactionDisabled ? "sample=1" : "sample=Redacted", failures);
        if (captureOptIn)
        {
            RequireTag(successSpan, "http.request.header.x-demo-req", "rv1", failures);
            RequireTag(successSpan, "http.response.header.x-demo-res", "sv1", failures);
        }
        else if (successSpan is not null &&
                 (successSpan.Tags.ContainsKey("http.request.header.x-demo-req") ||
                  successSpan.Tags.ContainsKey("http.response.header.x-demo-res")))
        {
            failures.Add("header attributes captured without opt-in");
        }

        foreach (var span in httpServerSpans)
        {
            if (!span.Tags.ContainsKey("url.path"))
                failures.Add("url.path missing on server span");

            var expectedName = span.Tags.TryGetValue("http.route", out var spanRoute)
                ? $"GET {spanRoute}"
                : "GET";
            if (!StringComparer.Ordinal.Equals(span.Name, expectedName))
                failures.Add($"unexpected high-cardinality span name: {span.Name}");
        }

        return new AspNetCoreReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

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

    private static void RequireStatus(CapturedActivity? activity, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!StringComparer.Ordinal.Equals(activity.Status, expected))
            failures.Add($"expected span status {expected}, got {activity.Status}");
    }
}

[JsonSerializable(typeof(AspNetCoreReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealAspNetCoreJsonContext : JsonSerializerContext;
