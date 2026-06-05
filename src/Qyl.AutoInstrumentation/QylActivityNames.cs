namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Activity Names.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylActivityNames);</code></example>
public static class QylActivityNames
{
    /// <summary>Well-known HTTP Client Request value used by qyl auto-instrumentation.</summary>
    public const string HttpClientRequest = "HTTP client request";
    /// <summary>Well-known HTTP Server Request value used by qyl auto-instrumentation.</summary>
    public const string HttpServerRequest = "HTTP server request";
    /// <summary>Well-known database Client Command value used by qyl auto-instrumentation.</summary>
    public const string DbClientCommand = "DB client command";
    /// <summary>Well-known Entity Framework Core Operation value used by qyl auto-instrumentation.</summary>
    public const string EntityFrameworkCoreOperation = "EF Core operation";
}
