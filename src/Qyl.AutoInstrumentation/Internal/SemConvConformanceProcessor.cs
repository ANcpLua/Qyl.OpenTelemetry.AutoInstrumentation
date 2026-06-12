using System.Diagnostics;

namespace Qyl.AutoInstrumentation.Internal;

/// <summary>
/// Conformance check: every emitted attribute key MUST exist in the qyl semconv registry. Preserved
/// from substrate-era M3 — the API is the same (an <see cref="ActivityListener"/> sees every span
/// stop and increments <see cref="QylSelfTelemetry.AttributeChecks"/> with a verdict), but the
/// registry lookup is now a build-time FrozenSet rather than reflection over assemblies, so the
/// processor is AOT/trim-clean.
///
/// <para>Safety invariants — unchanged from M3:</para>
/// <list type="bullet">
///   <item>Never throws into the app (errors swallowed).</item>
///   <item>Never writes to stdout/stderr (would violate Gate B no-behavior-change).</item>
///   <item>Never mutates the activity (would violate Gate A verified-OTLP).</item>
/// </list>
/// </summary>
internal static class SemConvConformanceProcessor
{
    private static int _explicitlyEnabled;
    private static int _listenerRegistered;

    internal static void Enable()
    {
        Interlocked.Exchange(ref _explicitlyEnabled, 1);
        EnsureListenerRegisteredIfEnabled();
    }

    internal static void EnsureListenerRegisteredIfEnabled()
    {
        if (!IsEnabled() || Interlocked.Exchange(ref _listenerRegistered, 1) == 1)
            return;

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QylActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped,
        });
    }

    /// <summary>
    /// Inspect a stopped <see cref="Activity"/> and emit one <c>qyl.semconv.attribute.checks</c>
    /// observation per attribute key.
    /// </summary>
    public static void OnActivityStopped(Activity activity)
    {
        if (!IsEnabled())
            return;

        try
        {
            foreach (var tag in activity.TagObjects)
            {
                var verdict = QylSemConvRegistry.IsKnownKey(tag.Key) ? "ok" : "unknown";
                QylSelfTelemetry.AttributeChecks.Add(
                    1, new KeyValuePair<string, object?>(QylSemanticAttributes.QylConformanceVerdict, verdict));
            }
        }
        catch (Exception exception)
        {
            QylSelfTelemetry.ConformanceProcessorFailures.Add(
                1,
                new KeyValuePair<string, object?>(QylSemanticAttributes.ExceptionType, exception.GetType().Name));
        }
    }

    internal static bool IsEnabled()
        => Volatile.Read(ref _explicitlyEnabled) is 1 ||
           QylAutoInstrumentationOptions.Current.ConformanceProcessorEnabled;
}
