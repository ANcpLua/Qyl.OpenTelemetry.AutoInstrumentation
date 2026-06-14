using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.Hosting;

var options = DemoOptions.Parse(args);
var captured = new List<CapturedActivity>();

using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

new ServiceCollection().AddQylAutoInstrumentation();

var scenarios = new[]
{
    DiagnosticScenario.Create(
        listenerName: "HttpHandlerDiagnosticListener",
        eventName: "qyl.http.client",
        domain: "http.client",
        expectedAttributes:
        [
            QylSemanticAttributes.QylInstrumentationDomain,
            QylSemanticAttributes.HttpRequestMethod,
            QylSemanticAttributes.ServerAddress,
            QylSemanticAttributes.HttpResponseStatusCode,
        ],
        forbiddenAttributes:
        [
            QylSemanticAttributes.UrlFull,
        ],
        attributes: new Dictionary<string, object?>
        {
            [QylSemanticAttributes.HttpRequestMethod] = "GET",
            [QylSemanticAttributes.UrlFull] = "https://qyl.local/live/client?id=42",
            [QylSemanticAttributes.ServerAddress] = "qyl.local",
            [QylSemanticAttributes.HttpResponseStatusCode] = 202,
        }),
    DiagnosticScenario.Create(
        listenerName: "Microsoft.AspNetCore",
        eventName: "qyl.http.server",
        domain: "http.server",
        expectedAttributes:
        [
            QylSemanticAttributes.QylInstrumentationDomain,
            QylSemanticAttributes.HttpRequestMethod,
            QylSemanticAttributes.HttpRoute,
            QylSemanticAttributes.HttpResponseStatusCode,
        ],
        forbiddenAttributes:
        [
            QylSemanticAttributes.UrlPath,
        ],
        attributes: new Dictionary<string, object?>
        {
            [QylSemanticAttributes.HttpRequestMethod] = "POST",
            [QylSemanticAttributes.HttpRoute] = "/qyl/live/{id}",
            [QylSemanticAttributes.UrlPath] = "/qyl/live/42",
            [QylSemanticAttributes.HttpResponseStatusCode] = 201,
        }),
    DiagnosticScenario.Create(
        listenerName: "Microsoft.EntityFrameworkCore",
        eventName: "qyl.db.efcore",
        domain: "db.efcore",
        expectedAttributes:
        [
            QylSemanticAttributes.QylInstrumentationDomain,
            QylSemanticAttributes.DbSystemName,
            QylSemanticAttributes.DbNamespace,
            QylSemanticAttributes.DbOperationName,
            QylSemanticAttributes.DbQuerySummary,
        ],
        forbiddenAttributes:
        [
            QylSemanticAttributes.DbQueryText,
        ],
        attributes: new Dictionary<string, object?>
        {
            [QylSemanticAttributes.DbSystemName] = "sqlite",
            [QylSemanticAttributes.DbNamespace] = "qyl_live",
            [QylSemanticAttributes.DbOperationName] = "SELECT",
            [QylSemanticAttributes.DbQuerySummary] = "SELECT qyl_live",
            [QylSemanticAttributes.DbQueryText] = "SELECT id, name FROM qyl_live WHERE id = 42",
        }),
    DiagnosticScenario.Create(
        listenerName: "SqlClientDiagnosticListener",
        eventName: "qyl.db.sqlclient",
        domain: "db.sqlclient",
        expectedAttributes:
        [
            QylSemanticAttributes.QylInstrumentationDomain,
            QylSemanticAttributes.DbSystemName,
            QylSemanticAttributes.DbNamespace,
            QylSemanticAttributes.DbOperationName,
            QylSemanticAttributes.DbQuerySummary,
            QylSemanticAttributes.ServerAddress,
        ],
        forbiddenAttributes:
        [
            QylSemanticAttributes.DbQueryText,
        ],
        attributes: new Dictionary<string, object?>
        {
            [QylSemanticAttributes.DbNamespace] = "qyl_live",
            [QylSemanticAttributes.DbOperationName] = "UPDATE",
            [QylSemanticAttributes.DbQuerySummary] = "UPDATE qyl_live",
            [QylSemanticAttributes.DbQueryText] = "UPDATE qyl_live SET seen = 1 WHERE id = 42",
            [QylSemanticAttributes.ServerAddress] = "localhost",
        }),
    DiagnosticScenario.Create(
        listenerName: "Grpc.Net.Client",
        eventName: "qyl.rpc.grpc",
        domain: "rpc.grpc",
        expectedAttributes:
        [
            QylSemanticAttributes.QylInstrumentationDomain,
            QylSemanticAttributes.RpcSystem,
            QylSemanticAttributes.RpcService,
            QylSemanticAttributes.RpcMethod,
            QylSemanticAttributes.ServerAddress,
            QylSemanticAttributes.ServerPort,
        ],
        forbiddenAttributes: [],
        attributes: new Dictionary<string, object?>
        {
            [QylSemanticAttributes.RpcService] = "qyl.LiveProbe",
            [QylSemanticAttributes.RpcMethod] = "Collect",
            [QylSemanticAttributes.ServerAddress] = "localhost",
            [QylSemanticAttributes.ServerPort] = 5001,
        }),
};

foreach (var scenario in scenarios)
    scenario.Emit();

var expectedDomains = scenarios.Select(static scenario => scenario.Domain).ToArray();
var missingDomains = expectedDomains
    .Where(domain => captured.All(activity => activity.Domain != domain))
    .ToArray();
var capturedByDomain = captured.ToDictionary(static activity => activity.Domain, StringComparer.Ordinal);
var missingAttributes = FindMissingAttributes(scenarios, capturedByDomain).ToArray();
var leakedAttributes = FindLeakedAttributes(scenarios, capturedByDomain).ToArray();

var report = new DemoReport(
    NativeAot: RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    ExpectedDomains: expectedDomains,
    MissingDomains: missingDomains,
    MissingAttributes: missingAttributes,
    LeakedAttributes: leakedAttributes,
    Activities: captured.OrderBy(static activity => activity.Domain, StringComparer.Ordinal).ToArray());

var json = JsonSerializer.Serialize(report, DemoJsonContext.Default.DemoReport);

if (!string.IsNullOrWhiteSpace(options.JsonPath))
    File.WriteAllText(options.JsonPath, json);

if (!string.IsNullOrWhiteSpace(options.HtmlPath))
    File.WriteAllText(options.HtmlPath, RenderHtml(report));

Console.WriteLine(json);

return missingDomains.Length is 0 && missingAttributes.Length is 0 && leakedAttributes.Length is 0 ? 0 : 1;

static string RenderHtml(DemoReport report)
{
    var status = report.MissingDomains.Length is 0 &&
                 report.MissingAttributes.Length is 0 &&
                 report.LeakedAttributes.Length is 0
        ? "PASS"
        : "FAIL";
    var rows = string.Join(
        Environment.NewLine,
        report.Activities.Select(static activity =>
        {
            var attributes = string.Join(
                string.Empty,
                activity.Tags.Select(static tag =>
                    $"<li><code>{Html(tag.Key)}</code><span>{Html(tag.Value)}</span></li>"));

            return $"""
                <section class="domain">
                  <header>
                    <strong>{Html(activity.Domain)}</strong>
                    <span>{Html(activity.Name)} - {Html(activity.Kind)}</span>
                  </header>
                  <ul>{attributes}</ul>
                </section>
                """;
        }));

    var html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>qyl live instrumentation evidence</title>
          <style>
            :root {
              color-scheme: light;
              font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
              background: #f5f7fb;
              color: #18202f;
            }
            body {
              margin: 0;
              padding: 32px;
            }
            main {
              max-width: 1120px;
              margin: 0 auto;
            }
            h1 {
              margin: 0 0 6px;
              font-size: 30px;
              line-height: 1.15;
            }
            .summary {
              display: grid;
              grid-template-columns: repeat(3, minmax(0, 1fr));
              gap: 12px;
              margin: 22px 0;
            }
            .metric, .domain {
              background: #ffffff;
              border: 1px solid #d9e1ef;
              border-radius: 8px;
              box-shadow: 0 1px 2px rgb(15 23 42 / 8%);
            }
            .metric {
              padding: 16px;
            }
            .metric span {
              display: block;
              color: #5f6f89;
              font-size: 13px;
            }
            .metric strong {
              display: block;
              margin-top: 6px;
              font-size: 24px;
            }
            .grid {
              display: grid;
              grid-template-columns: repeat(2, minmax(0, 1fr));
              gap: 14px;
            }
            .domain {
              overflow: hidden;
            }
            .domain header {
              display: flex;
              justify-content: space-between;
              gap: 16px;
              padding: 14px 16px;
              background: #e9f0ff;
              border-bottom: 1px solid #d9e1ef;
            }
            .domain header span {
              color: #44556e;
              text-align: right;
            }
            ul {
              list-style: none;
              margin: 0;
              padding: 12px 16px 16px;
            }
            li {
              display: grid;
              grid-template-columns: minmax(180px, 0.8fr) minmax(0, 1.2fr);
              gap: 16px;
              padding: 7px 0;
              border-bottom: 1px solid #edf1f7;
            }
            li:last-child {
              border-bottom: 0;
            }
            code {
              color: #1f5b8f;
              font-family: "SFMono-Regular", Consolas, monospace;
              font-size: 13px;
            }
            li span {
              overflow-wrap: anywhere;
            }
            @media (max-width: 760px) {
              body {
                padding: 18px;
              }
              .summary, .grid {
                grid-template-columns: 1fr;
              }
              .domain header, li {
                grid-template-columns: 1fr;
                display: grid;
              }
              .domain header span {
                text-align: left;
              }
            }
          </style>
        </head>
        <body>
          <main>
            <h1>qyl live instrumentation evidence</h1>
            <div class="summary">
              <div class="metric"><span>status</span><strong>__STATUS__</strong></div>
              <div class="metric"><span>domains captured</span><strong>__CAPTURED__/__EXPECTED__</strong></div>
              <div class="metric"><span>attribute misses</span><strong>__MISSING_ATTRIBUTES__</strong></div>
              <div class="metric"><span>privacy leaks</span><strong>__LEAKED_ATTRIBUTES__</strong></div>
              <div class="metric"><span>runtime mode</span><strong>__RUNTIME__</strong></div>
            </div>
            <div class="grid">
              __ROWS__
            </div>
          </main>
        </body>
        </html>
        """;

    return html
        .Replace("__STATUS__", Html(status), StringComparison.Ordinal)
        .Replace("__CAPTURED__", report.Activities.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__EXPECTED__", report.ExpectedDomains.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__MISSING_ATTRIBUTES__", report.MissingAttributes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__LEAKED_ATTRIBUTES__", report.LeakedAttributes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__RUNTIME__", Html(report.NativeAot), StringComparison.Ordinal)
        .Replace("__ROWS__", rows, StringComparison.Ordinal);
}

static string Html(string value) => WebUtility.HtmlEncode(value);

static IEnumerable<AttributeCheck> FindMissingAttributes(
    IEnumerable<DiagnosticScenario> scenarios,
    IReadOnlyDictionary<string, CapturedActivity> capturedByDomain)
{
    foreach (var scenario in scenarios)
    {
        if (!capturedByDomain.TryGetValue(scenario.Domain, out var activity))
            continue;

        foreach (var attribute in scenario.ExpectedAttributes)
        {
            if (!activity.Tags.ContainsKey(attribute))
                yield return new AttributeCheck(scenario.Domain, attribute);
        }
    }
}

static IEnumerable<AttributeCheck> FindLeakedAttributes(
    IEnumerable<DiagnosticScenario> scenarios,
    IReadOnlyDictionary<string, CapturedActivity> capturedByDomain)
{
    foreach (var scenario in scenarios)
    {
        if (!capturedByDomain.TryGetValue(scenario.Domain, out var activity))
            continue;

        foreach (var attribute in scenario.ForbiddenAttributes)
        {
            if (activity.Tags.ContainsKey(attribute))
                yield return new AttributeCheck(scenario.Domain, attribute);
        }
    }
}

internal sealed record DemoOptions(string? JsonPath, string? HtmlPath)
{
    public static DemoOptions Parse(string[] args)
    {
        string? jsonPath = null;
        string? htmlPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(args[index], "--json") && index + 1 < args.Length)
                jsonPath = args[++index];
            else if (StringComparer.Ordinal.Equals(args[index], "--html") && index + 1 < args.Length)
                htmlPath = args[++index];
        }

        return new DemoOptions(jsonPath, htmlPath);
    }
}

internal sealed record DiagnosticScenario(
    string ListenerName,
    string EventName,
    string Domain,
    IReadOnlyList<string> ExpectedAttributes,
    IReadOnlyList<string> ForbiddenAttributes,
    IReadOnlyDictionary<string, object?> Attributes)
{
    public static DiagnosticScenario Create(
        string listenerName,
        string eventName,
        string domain,
        IReadOnlyList<string> expectedAttributes,
        IReadOnlyList<string> forbiddenAttributes,
        IReadOnlyDictionary<string, object?> attributes)
        => new(listenerName, eventName, domain, expectedAttributes, forbiddenAttributes, attributes);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Synthetic producer only; the qyl runtime consumes DiagnosticSource and does not call Write.")]
    public void Emit()
    {
        using var diagnosticListener = new DiagnosticListener(ListenerName);
        if (diagnosticListener.IsEnabled(EventName))
            diagnosticListener.Write(EventName, Attributes);
    }
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Domain,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
    {
        var tags = activity.TagObjects.ToDictionary(
            static tag => tag.Key,
            static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            StringComparer.Ordinal);

        var domain = tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domainValue)
            ? domainValue
            : string.Empty;

        return new CapturedActivity(activity.DisplayName, activity.Kind.ToString(), domain, tags);
    }
}

internal sealed record AttributeCheck(string Domain, string Attribute);

internal sealed record DemoReport(
    string NativeAot,
    string[] ExpectedDomains,
    string[] MissingDomains,
    AttributeCheck[] MissingAttributes,
    AttributeCheck[] LeakedAttributes,
    CapturedActivity[] Activities);

[JsonSerializable(typeof(DemoReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class DemoJsonContext : JsonSerializerContext;
