namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>
/// The <c>ActivitySource</c> names the .NET framework itself declares. They are not vendor rows in
/// the semantic-convention registry — the runtime owns them — so they live here once and both the
/// enrichment lanes and <c>QylTelemetrySources</c>' <c>AddSource</c> calls read them from here.
/// </summary>
internal static class QylFrameworkActivitySources
{
    /// <summary>
    /// Kestrel's per-request source. Its <c>Microsoft.AspNetCore.Hosting.HttpRequestIn</c> activity
    /// is the one HTTP SERVER span of a request; qyl enriches it and creates none of its own.
    /// </summary>
    internal const string AspNetCore = "Microsoft.AspNetCore";

    /// <summary>The BCL's outbound HTTP source.</summary>
    internal const string HttpClient = "System.Net.Http";
}
