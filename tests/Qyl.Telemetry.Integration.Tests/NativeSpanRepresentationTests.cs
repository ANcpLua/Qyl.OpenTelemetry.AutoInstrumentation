using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using GenAiAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.GenAi.GenAiAttributes;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;

namespace Qyl.Telemetry.Integration.Tests;

[TestClass]
public sealed class NativeSpanRepresentationTests
{
    [TestMethod]
    [DataRow("Experimental.Microsoft.Extensions.AI")]
    [DataRow("Experimental.Microsoft.Agents.AI")]
    public void Known_AI_finish_reason_arrays_convert_losslessly_and_invalid_values_remain_visible(string sourceName)
    {
        WithPipeline(sourceName, source =>
        {
            Assert.AreSequenceEqual(["stop", "", "line\nbreak", "quoted\"value"],
                Assert.IsInstanceOfType<string[]>(Export(source, GenAiAttributes.ResponseFinishReasons,
                    "[\"stop\",\"\",\"line\\nbreak\",\"quoted\\\"value\"]")));
            Assert.IsEmpty(Assert.IsInstanceOfType<string[]>(Export(source, GenAiAttributes.ResponseFinishReasons, "[]")));
            foreach (var original in new[] { "[\"stop\",1]", "[null]", "{}", "null", "\"stop\"", "{" })
                Assert.AreEqual(original, Export(source, GenAiAttributes.ResponseFinishReasons, original));
        });
    }

    [TestMethod]
    public void MassTransit_message_sizes_preserve_exact_nonnegative_Int64_and_retain_unconvertible_values()
    {
        WithPipeline("MassTransit", source =>
        {
            Assert.AreEqual(0L, Assert.IsInstanceOfType<long>(Export(source, MessagingAttributes.MessageBodySize, "0")));
            Assert.AreEqual(long.MaxValue, Assert.IsInstanceOfType<long>(Export(source, MessagingAttributes.MessageBodySize, "9223372036854775807")));
            foreach (var original in new[] { "9223372036854775808", "-1", "+1", "1.0", " 1", "1 ", "", "bytes" })
                Assert.AreEqual(original, Export(source, MessagingAttributes.MessageBodySize, original));
        });
    }

    [TestMethod]
    public void Other_producers_and_unrelated_attributes_are_not_converted()
    {
        WithPipeline("qyl.tests.other-producer", source =>
        {
            Assert.AreEqual("[\"stop\"]", Export(source, GenAiAttributes.ResponseFinishReasons, "[\"stop\"]"));
            Assert.AreEqual("42", Export(source, MessagingAttributes.MessageBodySize, "42"));
        });
        WithPipeline("MassTransit", source =>
            Assert.AreEqual("42", Export(source, "qyl.app.text", "42")));
    }

    private readonly List<Activity> _exported = [];

    private object? Export(ActivitySource source, string key, string value)
    {
        _exported.Clear();
        using (var activity = source.StartActivity("wire-representation"))
        {
            Assert.IsNotNull(activity);
            activity.SetTag(key, value);
        }
        return Assert.ContainsSingle(_exported).GetTagItem(key);
    }

    private void WithPipeline(string sourceName, Action<ActivitySource> exercise)
    {
        var previousEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
            var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.AddQyl(options =>
            {
                options.RequireConfiguredEndpoint = true;
                options.EnableCollectorDiscovery = false;
                options.EnableLogExport = false;
                options.EnableMetricsExport = false;
                options.AdditionalSources.Add(sourceName);
                options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(_exported);
            });
            using var host = builder.Build();
            _ = host.Services.GetRequiredService<TracerProvider>();
            using var source = new ActivitySource(sourceName);
            exercise(source);
        }
        finally
        {
            _exported.Clear();
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", previousEndpoint);
        }
    }
}
