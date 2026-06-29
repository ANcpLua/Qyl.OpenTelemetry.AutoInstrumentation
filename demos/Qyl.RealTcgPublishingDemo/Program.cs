using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.Publishing;

// Proof: the Publishing package emits the binary's Telemetry Capability Graph as a real OTel
// LogRecord at host startup. We attach a processor (the OTel SDK's pipeline — exactly what an OTLP
// exporter would sit behind) and assert the captured record matches the binary's own TCG.
var capturer = new CapturingProcessor();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.AddProcessor(capturer);
});
builder.Services.AddQylTelemetryCapabilityGraphPublisher();

using (var host = builder.Build())
{
    await host.StartAsync();
    await host.StopAsync();
}

if (!capturer.Found)
{
    await Console.Error.WriteLineAsync("FAIL: no LogRecord carrying qyl.tcg.schema_version was emitted");
    return 1;
}

Console.WriteLine("event=" + capturer.EventName);
Console.WriteLine("schema_version=" + capturer.SchemaVersion);
Console.WriteLine("capability_count=" + capturer.CapabilityCount);
Console.WriteLine("body_is_tcg_json=" + (capturer.Body == QylTelemetryCapabilityGraph.Json).ToString(CultureInfo.InvariantCulture));

var expectedCount = QylTelemetryCapabilityGraph.CapabilityCount.ToString(CultureInfo.InvariantCulture);
var ok =
    capturer.EventName == "qyl.telemetry_capability_graph" &&
    capturer.SchemaVersion == QylTelemetryCapabilityGraph.SchemaVersion &&
    capturer.CapabilityCount == expectedCount &&
    capturer.Body == QylTelemetryCapabilityGraph.Json;

if (!ok)
{
    await Console.Error.WriteLineAsync("FAIL: emitted LogRecord did not match the binary's TCG");
    return 1;
}

Console.WriteLine("tcg-publishing-ok");
return 0;

// Copies the fields out at OnEnd (synchronously, during the log call) so LogRecord pooling cannot
// recycle them out from under the assertions above.
internal sealed class CapturingProcessor : BaseProcessor<LogRecord>
{
    public bool Found { get; private set; }
    public string? EventName { get; private set; }
    public string? SchemaVersion { get; private set; }
    public string? CapabilityCount { get; private set; }
    public string? Body { get; private set; }

    public override void OnEnd(LogRecord record)
    {
        if (record.Attributes is null)
            return;

        if (Attribute(record, "qyl.tcg.schema_version") is { } schemaVersion)
        {
            Found = true;
            EventName = record.EventId.Name;
            SchemaVersion = schemaVersion;
            CapabilityCount = Attribute(record, "qyl.tcg.capability_count");
            Body = record.FormattedMessage ?? record.Body;
        }
    }

    private static string? Attribute(LogRecord record, string key)
    {
        foreach (var attribute in record.Attributes!)
        {
            if (attribute.Key == key)
                return attribute.Value switch
                {
                    null => null,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => attribute.Value.ToString(),
                };
        }

        return null;
    }
}
