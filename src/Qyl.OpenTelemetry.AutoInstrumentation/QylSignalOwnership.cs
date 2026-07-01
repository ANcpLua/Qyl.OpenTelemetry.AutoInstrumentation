using System.Collections.Concurrent;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Single-owner-per-signal registry. More than one lane can be able to produce a span for the same
/// instrumentation signal — a source-generated call-site interceptor, a generated middleware/wrapper,
/// the library's own native <c>ActivitySource</c>, or a <c>DiagnosticListener</c> subscription. Without
/// coordination a consumer that opts into two lanes emits the operation twice (double-counting).
///
/// <para>
/// Each lane <see cref="Register"/>s a priority for a signal; only the highest-priority registered lane
/// <see cref="ShouldEmit"/>s, every other lane defers — so exactly one span is produced per operation.
/// Priorities follow the qyl instrumentation-mechanism ranking (higher = more precise, lower overhead,
/// more AOT-native): compiler-generated &gt; interceptor &gt; generated middleware &gt; native
/// ActivitySource &gt; DiagnosticListener.
/// </para>
///
/// <para>
/// Registration is race-free by construction: the interceptor helper's static initializer registers on
/// first use — which happens inside the intercepted call, before that call reaches the underlying
/// framework code that raises the DiagnosticListener event — and the middleware registers at DI time,
/// before the first request. So by the time a lower lane observes an event, the higher lane is already
/// the recorded owner.
/// </para>
/// </summary>
internal static class QylSignalOwnership
{
    /// <summary>Compiler-generated AOP (reserved for a future lane).</summary>
    public const int CompilerGenerated = 100;

    /// <summary>Source-generated call-site interceptor — the qyl-preferred lane.</summary>
    public const int Interceptor = 95;

    /// <summary>Generated middleware / wrapper (e.g. the ASP.NET Core server-span <c>IStartupFilter</c>).</summary>
    public const int GeneratedMiddleware = 90;

    /// <summary>A span emitted natively by the instrumented library's own <c>ActivitySource</c>.</summary>
    public const int NativeActivitySource = 85;

    /// <summary>A <c>DiagnosticListener</c> subscription — broad compatibility, coarser payload.</summary>
    public const int DiagnosticListener = 70;

    private static readonly ConcurrentDictionary<string, int> Owners = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that a lane of the given <paramref name="priority"/> can produce
    /// <paramref name="instrumentationId"/>. The highest priority ever registered wins.
    /// </summary>
    public static void Register(string instrumentationId, int priority)
        => Owners.AddOrUpdate(instrumentationId, priority, (_, existing) => existing >= priority ? existing : priority);

    /// <summary>
    /// True when a lane of <paramref name="priority"/> is the highest-priority producer registered for
    /// <paramref name="instrumentationId"/> and should therefore emit; false when a higher lane owns the
    /// signal, so this lane defers. When nothing is registered the caller emits (it is the only lane).
    /// </summary>
    public static bool ShouldEmit(string instrumentationId, int priority)
        => !Owners.TryGetValue(instrumentationId, out var max) || priority >= max;
}
