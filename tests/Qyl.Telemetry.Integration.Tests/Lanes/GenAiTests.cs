using System.Text.Json;

namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// Microsoft.Extensions.AI, Microsoft.Agents.AI and Workflows through their UseOpenTelemetry opt-ins,
/// managed only, with each per-instrumentation toggle off in turn and all of them off together. What
/// Qyl.Telemetry.Hosting exported is checked in the report and, span by span, in the collector.
/// </summary>
[TestClass]
[TestCategory("genai")]
public sealed class GenAiTests(TestContext testContext)
{
    private const string MeaiSource = "Experimental.Microsoft.Extensions.AI";
    private const string AgentsSource = "Experimental.Microsoft.Agents.AI";
    private const string WorkflowsSource = "Microsoft.Agents.AI.Workflows";
    private static readonly string[] s_sources = [MeaiSource, AgentsSource, WorkflowsSource];
    private static readonly string[] s_meters = [AgentsSource, MeaiSource];

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("genai"), testContext);
        lane.BuildManaged("--disable-build-servers");
        Verify(lane, (await lane.RunManagedAsync("managed GenAI demo")), s_sources, s_meters);

        (string Name, string Variable, string[] Sources, string[] Meters)[] scenarios =
        [
            ("MEAI traces disabled", "OTEL_DOTNET_AUTO_TRACES_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED", Without(s_sources, MeaiSource), s_meters),
            ("MEAI metrics disabled", "OTEL_DOTNET_AUTO_METRICS_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED", s_sources, Without(s_meters, MeaiSource)),
            ("Agents traces disabled", "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED", Without(s_sources, AgentsSource), s_meters),
            ("Agents metrics disabled", "OTEL_DOTNET_AUTO_METRICS_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED", s_sources, Without(s_meters, AgentsSource)),
            ("Workflows traces disabled", "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAIWORKFLOWS_INSTRUMENTATION_ENABLED", Without(s_sources, WorkflowsSource), s_meters),
        ];
        foreach (var (name, variable, sources, meters) in scenarios)
            Verify(lane, (await lane.RunManagedAsync($"managed GenAI demo ({name})", lane.With((variable, "false")))), sources, meters);

        Verify(lane, (await lane.RunManagedAsync("managed GenAI demo (all registrations disabled)",
            lane.With([.. scenarios.Select(static scenario => (scenario.Variable, "false"))]))), [], []);
    }

    private static string[] Without(string[] values, string removed) => values.Where(value => value != removed).ToArray();

    private static void Verify(DemoContext lane, DemoRun run, string[] expectedSources, string[] expectedMeters)
    {
        run.RequireSucceeded();
        Assert.IsTrue(run.StringField("RuntimeMode") == "dynamic-code-supported", $"{run.Label} runtime mode mismatch: {run.StringField("RuntimeMode")}");
        run.RequirePass()
            .RequireBool("BareResponseVerified", true)
            .RequireBool("AgentResponseVerified", true)
            .RequireBool("WorkflowOutputVerified", true);

        var activities = run.Array("Activities");
        var sourceNames = activities.EnumerateArray().Select(static activity => ReportedActivity.Text(activity, "SourceName")).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(sourceNames.SetEquals(s_sources), $"{run.Label} source mismatch: expected [{string.Join(", ", s_sources)}] actual [{string.Join(", ", sourceNames)}]");
        LaneChecks.ExactStrings(run, "PublishedMeterNames", s_meters.Order(StringComparer.Ordinal).ToArray());
        var measurements = run.RequireCount("Measurements", 14, "measurements");
        LaneChecks.ExactStrings(run, "QylExportedActivitySources", expectedSources.Order(StringComparer.Ordinal).ToArray());
        var exportedMeters = DemoRun.Strings(run.Array("QylExportedMeterNames")).Where(meter => s_meters.Contains(meter)).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(exportedMeters.SetEquals(expectedMeters), $"{run.Label} Qyl.Telemetry.Hosting exported meter mismatch: expected [{string.Join(", ", expectedMeters.Order(StringComparer.Ordinal))}] actual [{string.Join(", ", exportedMeters.Order(StringComparer.Ordinal))}]");

        var capturedMeters = run.Telemetry.Metrics.Select(static metric => metric.Scope?.Name)
            .OfType<string>().Where(meter => s_meters.Contains(meter)).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(capturedMeters.SetEquals(expectedMeters), $"{run.Label}: actual OTLP instrumentation scopes differ from the enabled GenAI meters");

        // What reached the collector is exactly the spans of the subscribed sources, nothing more.
        var exported = activities.EnumerateArray().Where(activity => expectedSources.Contains(ReportedActivity.Text(activity, "SourceName"))).ToArray();
        lane.Telemetry.ServiceSpansExactly(run, exported.Select(static activity => ReportedActivity.Text(activity, "SpanId")!).ToArray(), "GenAI");
        lane.Telemetry.Spans(run, JsonSerializer.SerializeToElement(exported), "GenAI");
        var exportedMeasurements = measurements.EnumerateArray()
            .Where(measurement => expectedMeters.Contains(ReportedActivity.Text(measurement, "MeterName")))
            .ToArray();
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(JsonSerializer.SerializeToElement(exportedMeasurements), "InstrumentName"));
    }
}
