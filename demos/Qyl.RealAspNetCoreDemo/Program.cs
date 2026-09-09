using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using SessionAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Session.SessionAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

// The real registration path: AddQyl subscribes ASP.NET Core's own Microsoft.AspNetCore source and
// registers the middleware that enriches its activity. qyl starts no server span of its own, so
// what this demo exports is what a consumer's collector receives — one SERVER span per request.
var exported = new List<Activity>();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddHealthChecks();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-aspnetcore-demo";
    // The live-check gate points this at its OTLP listener. Unset, the demo exports into a
    // closed port and asserts on its in-memory exporter alone.
    options.CollectorEndpoint =
        new Uri(Environment.GetEnvironmentVariable("QYL_LIVE_CHECK_ENDPOINT") ?? "http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.AdditionalSources.Add(DemoWork.SourceName);
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exported));

// Registered after AddQyl, exactly as a Qyl.Api application registers its session-baggage filter:
// qyl's own filter stays outermost, and Activity.Current in this one is the hosting activity that
// the whole request hangs off.
builder.Services.AddSingleton<IStartupFilter, SessionStartupFilter>();

var app = builder.Build();

app.MapHealthChecks("/healthz");
app.MapGet("/items/{id:int}", (HttpContext context) =>
{
    // A child span of the request, so the session tag the filter puts on the hosting activity has
    // something to reach.
    using (DemoWork.Source.StartActivity(DemoWork.ChildSpanName))
    {
    }

    context.Response.Headers["X-Demo-Res"] = "sv1";
    context.Response.StatusCode = StatusCodes.Status204NoContent;
    return Task.CompletedTask;
});
app.MapGet("/fail/{id:int}", (HttpContext _) => throw new InvalidOperationException("expected route failure"));
// A request whose client goes away: the handler waits on RequestAborted and the cancellation
// unwinds through the pipeline. Nothing is sent, and the span must not claim a 500 for it.
app.MapGet("/hang/{id:int}", async (HttpContext context) =>
{
    await Task.Delay(Timeout.Infinite, context.RequestAborted);
});

await app.StartAsync();

try
{
    var address = app.Urls.Single();
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("X-Demo-Req", "rv1");
    httpClient.DefaultRequestHeaders.Add("baggage", "session.id=" + AspNetCoreReport.SessionId);
    using (await httpClient.GetAsync($"{address}/items/42?sample=1"))
    {
    }

    using (await httpClient.GetAsync($"{address}/fail/13?sample=1"))
    {
    }

    // A request that resolves no endpoint. It leaves routing without an http.route, and its span has
    // to be named after its method alone rather than the framework's raw operation name.
    using (await httpClient.GetAsync($"{address}/nope?sample=1"))
    {
    }

    // The client disconnects while the handler is still waiting. Kestrel classifies that as 499 and
    // not as an application error; the span is awaited here because its end is on the server's clock,
    // not the client's.
    var disconnected = false;
    using (var disconnect = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
    {
        try
        {
            using (await httpClient.GetAsync($"{address}/hang/7?sample=1", disconnect.Token))
            {
            }
        }
        catch (OperationCanceledException) when (disconnect.IsCancellationRequested)
        {
            disconnected = true;
        }
    }

    if (!disconnected)
        throw new InvalidOperationException("the /hang request completed instead of being cancelled");

    for (var attempt = 0; attempt < 200 && !exported.Any(static activity =>
             activity.Kind is ActivityKind.Server &&
             activity.GetTagItem(HttpAttributes.ResponseStatusCode) is int status && status is 499); attempt++)
    {
        await Task.Delay(50);
    }
}
finally
{
    await app.StopAsync();
}

app.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);

var report = AspNetCoreReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exported.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealAspNetCoreJsonContext.Default.AspNetCoreReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

/// <summary>The application's own span, and the source AddQyl subscribes through AdditionalSources.</summary>
internal static class DemoWork
{
    public const string SourceName = "Qyl.RealAspNetCoreDemo";

    public const string ChildSpanName = "demo child work";

    public static ActivitySource Source { get; } = new(SourceName);
}

/// <summary>
/// Stands in for <c>Qyl.Api</c>'s session-baggage filter: it reads the agent's <c>baggage</c> header
/// and stamps <c>session.id</c> on whatever activity is current. Nothing here knows which activity
/// that is — which is the point of the assertion it feeds.
/// </summary>
internal sealed class SessionStartupFilter : IStartupFilter
{
    private const string Member = SessionAttributes.Id + "=";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(static (context, nextMiddleware) =>
            {
                var header = context.Request.Headers["baggage"].ToString();
                if (Activity.Current is { } activity && header.StartsWith(Member, StringComparison.Ordinal))
                    activity.SetTag(SessionAttributes.Id, header[Member.Length..]);

                return nextMiddleware(context);
            });

            next(app);
        };
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
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
    /// <summary>The session an agent claims through the <c>baggage</c> header.</summary>
    public const string SessionId = "qyl-real-aspnetcore-demo-session";

    private const string RequestHeader = HttpAttributes.RequestHeader + ".x-demo-req";
    private const string ResponseHeader = HttpAttributes.ResponseHeader + ".x-demo-res";
    private const string SessionIdTag = SessionAttributes.Id;
    private const string AspNetCoreSource = "Microsoft.AspNetCore";

    public static AspNetCoreReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();

        // One SERVER span per request, and it is ASP.NET Core's own. A qyl-made server span — from
        // a middleware or a DiagnosticListener adapter — would show up here as an extra span, or as
        // one whose source is not the framework's.
        var serverSpans = activities.Where(static activity => activity.Kind is "Server").ToArray();
        if (serverSpans.Length != 4)
            failures.Add($"expected exactly one server span for each of the 4 requests, got {serverSpans.Length}");

        foreach (var span in serverSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Source, AspNetCoreSource))
                failures.Add($"server span from '{span.Source}' rather than ASP.NET Core's own source: {span.Name}");
        }

        var httpServerSpans = serverSpans
            .Where(static activity =>
                activity.Tags.TryGetValue(QylAttributes.InstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylAttributes.InstrumentationDomainValues.AspNetCoreServer))
            .ToArray();

        if (httpServerSpans.Length != serverSpans.Length)
            failures.Add($"{serverSpans.Length - httpServerSpans.Length} server span(s) carry no qyl.instrumentation.domain");

        var successSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue(HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "204"));
        var failureSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue(HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "500"));
        var routelessSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue(HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "404"));
        var disconnectSpan = httpServerSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue(HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "499"));

        Require(successSpan, "204 route span", failures);
        Require(failureSpan, "500 route span", failures);
        Require(routelessSpan, "404 routeless span", failures);
        Require(disconnectSpan, "499 client-disconnect span", failures);
        // The client went away before anything was sent. That is Kestrel's 499, not a failure of the
        // application: no error.type, and the span status stays unset.
        if (disconnectSpan is not null && disconnectSpan.Tags.TryGetValue(ErrorAttributes.Type, out var disconnectError))
            failures.Add($"client-disconnect span carries {ErrorAttributes.Type}={disconnectError}");
        RequireTag(disconnectSpan, HttpAttributes.Route, "/hang/{id:int}", failures);
        RequireStatus(disconnectSpan, "Unset", failures);
        // http.route is conditionally required: the request resolved no endpoint, so there is no
        // template to record. The span name below is what has to survive that.
        if (routelessSpan is not null && routelessSpan.Tags.TryGetValue(HttpAttributes.Route, out var absentRoute))
            failures.Add($"routeless request carries {HttpAttributes.Route}={absentRoute}");
        RequireTag(successSpan, HttpAttributes.RequestMethod, HttpAttributes.RequestMethodValues.Get, failures);
        RequireTag(successSpan, HttpAttributes.Route, "/items/{id:int}", failures);
        RequireTag(failureSpan, HttpAttributes.Route, "/fail/{id:int}", failures);
        // The unhandled exception is what failed the request, so error.type is its type name rather
        // than the status code the server sends afterwards.
        RequireTag(failureSpan, ErrorAttributes.Type, "System.InvalidOperationException", failures);
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Error", failures);

        // The session: an agent's baggage header, stamped by the application's own filter onto
        // whatever Activity.Current is — the hosting activity — and copied from there onto the
        // request's child spans by QylSessionSpanProcessor.
        RequireTag(successSpan, SessionIdTag, SessionId, failures);
        var childSpan = activities.FirstOrDefault(static activity =>
            StringComparer.Ordinal.Equals(activity.Source, DemoWork.SourceName));
        Require(childSpan, "child span of the session request", failures);
        RequireTag(childSpan, SessionIdTag, SessionId, failures);

        // Option rows are asserted in both directions, keyed off the same env vars
        // the runtime honors: header capture opt-in and URL query redaction.
        var captureOptIn = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"));
        var redactionDisabled = string.Equals(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        RequireTag(successSpan, UrlAttributes.Query, redactionDisabled ? "sample=1" : "sample=Redacted", failures);
        if (captureOptIn)
        {
            RequireTag(successSpan, RequestHeader, "rv1", failures);
            RequireTag(successSpan, ResponseHeader, "sv1", failures);
        }
        else if (successSpan is not null &&
                 (successSpan.Tags.ContainsKey(RequestHeader) ||
                  successSpan.Tags.ContainsKey(ResponseHeader)))
        {
            failures.Add("header attributes captured without opt-in");
        }

        foreach (var span in httpServerSpans)
        {
            if (!span.Tags.ContainsKey(UrlAttributes.Path))
                failures.Add("url.path missing on server span");

            var expectedName = span.Tags.TryGetValue(HttpAttributes.Route, out var spanRoute)
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
