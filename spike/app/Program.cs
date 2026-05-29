// qyl walking-skeleton target app — UNMODIFIED workload.
//
// No telemetry code here on purpose: auto-instrumentation must produce the span without
// touching this file.
//
// IMPORTANT fixture invariant: the app's observable output is DETERMINISTIC and INDEPENDENT of
// the HTTP outcome. The GET exists only to generate a CLIENT span for Gate A; its result is
// deliberately discarded. Coupling output to the network result would make Gate B
// (no-behavior-change) hostage to flaky connectivity — which is a fixture flaw, not a finding.

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

try
{
    // A CLIENT span is emitted around this send whether it succeeds, faults, or times out.
    using var _ = await client.GetAsync("https://example.com");
}
catch
{
    // Outcome intentionally ignored — see fixture invariant above.
}

Console.WriteLine("APP_DONE");
