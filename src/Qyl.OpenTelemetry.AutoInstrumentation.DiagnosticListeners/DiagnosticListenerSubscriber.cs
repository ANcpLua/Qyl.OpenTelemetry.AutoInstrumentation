using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners;

/// <summary>
/// Base subscriber for the AOT-native instrumentation model.
///
/// <para>
/// Pre-swap: each library (HttpClient, EFCore, …) was instrumented by an IL-rewriting CallTarget
/// integration injected by the substrate's CLR profiler. Post-swap: we subscribe to the same
/// libraries' built-in <see cref="DiagnosticListener"/> events. DiagnosticSource is a managed BCL
/// primitive that's been AOT-safe since .NET 8, so this layer emits spans without any IL rewriting
/// or runtime code generation. Concrete subscribers react on the completion (<c>*.Stop</c>) event and
/// stamp the span to the ambient framework activity's start (via
/// <c>QylActivitySource.StartAtAmbientStart</c>) so the emitted duration reflects the real operation,
/// not a ~0 span.
/// </para>
/// </summary>
public abstract class DiagnosticListenerSubscriber : IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private IDisposable? _allListenersSubscription;
    private IDisposable? _eventSubscription;

    /// <summary>The well-known <see cref="DiagnosticListener"/> name to subscribe to
    /// (e.g. <c>HttpHandlerDiagnosticListener</c>, <c>Microsoft.AspNetCore</c>).</summary>
    protected abstract string ListenerName { get; }

    /// <summary>The signal controlled by the OTEL_DOTNET_AUTO_* env var family.</summary>
    protected abstract QylAutoInstrumentationSignal Signal { get; }

    /// <summary>The instrumentation id used in OTEL_DOTNET_AUTO_{SIGNAL}_{ID}_INSTRUMENTATION_ENABLED.</summary>
    protected abstract string InstrumentationId { get; }

    /// <summary>Subscribe to <see cref="DiagnosticListener.AllListeners"/> and wait for the
    /// target listener to appear. Idempotent; safe under <c>[ModuleInitializer]</c>.</summary>
    public void Subscribe()
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(Signal, InstrumentationId))
            return;

        // Claim the DiagnosticListener lane for this signal. If a higher-priority lane (interceptor /
        // generated middleware) also covers it, this subscriber defers in OnNext so the operation is
        // instrumented exactly once. See QylSignalOwnership.
        QylSignalOwnership.Register(InstrumentationId, QylSignalOwnership.DiagnosticListener);
        _allListenersSubscription ??= DiagnosticListener.AllListeners.Subscribe(new AllListenersObserver(this));
    }

    /// <summary>Implement to react to a single event from the subscribed listener.</summary>
    protected abstract void OnEvent(string name, object? payload);

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(Signal, InstrumentationId)
            && QylSignalOwnership.ShouldEmit(InstrumentationId, QylSignalOwnership.DiagnosticListener))
            OnEvent(value.Key, value.Value);
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { /* invisible */ }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { /* invisible */ }

    /// <summary>Tear down both subscriptions.</summary>
    public void Dispose()
    {
        _eventSubscription?.Dispose();
        _allListenersSubscription?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class AllListenersObserver(DiagnosticListenerSubscriber owner) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == owner.ListenerName)
                owner._eventSubscription = value.Subscribe(owner);
        }

        public void OnError(Exception error) { }

        public void OnCompleted() { }
    }
}
