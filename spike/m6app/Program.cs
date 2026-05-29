// qyl M6 fixture — proves the LOGS pipeline with trace correlation.
// Logs an ILogger record INSIDE an active span; the substrate's OTel logger provider (injected
// by auto-instrumentation) must capture it and stamp it with the active trace_id/span_id.
//
// No console logging provider is registered on purpose: only the substrate-injected OTel
// provider should capture the log, so the app's own stdout stays clean (Gate B).
// Span source captured via OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES="Qyl.M6.Demo".

using System.Diagnostics;
using Microsoft.Extensions.Logging;

using var source = new ActivitySource("Qyl.M6.Demo");
using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Qyl.M6");

using (source.StartActivity("m6-log-correlation"))
{
    logger.LogInformation("qyl-m6 log inside active span");
}

Console.WriteLine("M6_DONE");
