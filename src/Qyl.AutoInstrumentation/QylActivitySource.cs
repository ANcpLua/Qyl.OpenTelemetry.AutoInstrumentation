using System.Diagnostics;
using System.Reflection;

namespace Qyl.AutoInstrumentation;

/// <summary>
/// The single qyl-owned <see cref="ActivitySource"/>. All spans authored by qyl flow through here,
/// so a consuming app subscribes ONCE and sees every qyl-emitted span across every instrumentation
/// module (HTTP, EFCore, SqlClient, gRPC, …).
/// </summary>
public static class QylActivitySource
{
    /// <summary>The well-known source name. Mirror this in <c>AddSource(...)</c> on a TracerProvider
    /// or in <c>OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES</c>.</summary>
    public const string Name = "Qyl.AutoInstrumentation";

    /// <summary>The single source instance.</summary>
    public static readonly ActivitySource Source = new(
        Name,
        typeof(QylActivitySource).Assembly.GetName().Version?.ToString() ?? "0.0.0");
}
