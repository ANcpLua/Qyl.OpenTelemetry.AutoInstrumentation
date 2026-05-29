// qyl walking-skeleton target app — UNMODIFIED workload.
// No telemetry code here on purpose: auto-instrumentation must produce the span
// without touching this file. Output is deterministic enough for Gate B (no-behavior-change):
// the same line prints with or without qyl/OTel attached.

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

try
{
    using var resp = await client.GetAsync("https://example.com");
    Console.WriteLine($"APP_OUTPUT status={(int)resp.StatusCode}");
}
catch (Exception ex)
{
    // An HttpClient CLIENT span is still produced even when the request faults,
    // so Gate A has a span to assert on regardless of network outcome.
    Console.WriteLine($"APP_OUTPUT error={ex.GetType().Name}");
}

Console.WriteLine("APP_DONE");
