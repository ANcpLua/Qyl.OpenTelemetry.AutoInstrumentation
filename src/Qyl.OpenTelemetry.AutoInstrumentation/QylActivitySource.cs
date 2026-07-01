using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// The single qyl-owned <see cref="ActivitySource"/>. All spans authored by qyl flow through here,
/// so a consuming app subscribes ONCE and sees every qyl-emitted span across every instrumentation
/// module (HTTP, EFCore, SqlClient, gRPC, …).
/// </summary>
public static class QylActivitySource
{
    /// <summary>The well-known source name. Mirror this in <c>AddSource(...)</c> on a TracerProvider
    /// or in <c>OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES</c>.</summary>
    public const string Name = "Qyl.OpenTelemetry.AutoInstrumentation";

    /// <summary>The single source instance.</summary>
    public static readonly ActivitySource Source = new(
        Name,
        QylInstrumentation.Version);

    internal static bool IsRecordingEnabled
        => Source.HasListeners();

    internal static Activity? StartActivity(string operationName, ActivityKind activityKind)
        => Source.HasListeners()
            ? Source.StartActivity(operationName, activityKind)
            : null;

    /// <summary>
    /// Starts a qyl span stamped to the ambient (framework) <see cref="Activity"/>'s real start time,
    /// so DiagnosticListener bridges that only observe the completion (<c>*.Stop</c>) event emit the
    /// operation's TRUE duration instead of a ~0 span. Parents to the current activity to preserve
    /// trace correlation; falls back to a now-stamped span when there is no ambient activity.
    /// </summary>
    internal static Activity? StartAtAmbientStart(string operationName, ActivityKind activityKind)
    {
        if (!Source.HasListeners())
            return null;

        var ambient = Activity.Current;
        return ambient is null
            ? Source.StartActivity(operationName, activityKind)
            : Source.StartActivity(operationName, activityKind, ambient.Context, tags: null, links: null, startTime: ambient.StartTimeUtc);
    }
}
