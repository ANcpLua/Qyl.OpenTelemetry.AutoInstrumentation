// qyl M8 fixture — first qyl-authored DOMAIN instrumentation: a GenAI span.
// Emits a semconv-shaped gen_ai.* CLIENT span via a qyl ActivitySource, captured by the
// substrate via OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES="Qyl.M8.GenAI".
// The conformance plugin checks every gen_ai.* key against the 922-key registry.
// Observable output is deterministic (M8_DONE) and independent of attach — for Gate B.

using System.Diagnostics;

using var source = new ActivitySource("Qyl.M8.GenAI");

// Span name follows the GenAI convention: "{operation} {request.model}".
using (var act = source.StartActivity("chat gpt-4", ActivityKind.Client))
{
    act?.SetTag("gen_ai.operation.name", "chat");
    act?.SetTag("gen_ai.system", "openai");
    act?.SetTag("gen_ai.request.model", "gpt-4");
    act?.SetTag("gen_ai.response.model", "gpt-4");
    act?.SetTag("gen_ai.usage.input_tokens", 10);
    act?.SetTag("gen_ai.usage.output_tokens", 20);
}

Console.WriteLine("M8_DONE");
