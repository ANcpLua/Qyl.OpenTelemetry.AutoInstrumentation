using Microsoft.CodeAnalysis;

namespace Qyl.Telemetry.AutoInstrumentation.SourceGenerators;

/// <summary>
/// Diagnostics reported by <see cref="QylAutoInstrumentationGenerator"/>.
/// </summary>
/// <remarks>
/// The generator emits interceptors for call sites that match a declared integration. A call site
/// that names a declared receiver and method but whose signature does not fit the declared shape is
/// skipped: emitting an interceptor with a mismatched signature would break the consumer's build,
/// and skipping in silence would hide the loss of instrumentation. It is reported instead, so a
/// library version that changes an intercepted signature is visible in the build log rather than
/// discovered as missing telemetry.
/// </remarks>
internal static class QylGeneratorDiagnostics
{
    /// <summary>
    /// Reported when a call site names a declared integration receiver and method but does not fit
    /// the declared interceptor shape, so no interceptor is emitted for it.
    /// </summary>
    internal static readonly DiagnosticDescriptor ShapeNotMatched = new(
        id: "QYL1001",
        title: "Declared qyl integration call site does not match the interceptor shape",
        messageFormat:
            "No qyl interceptor was emitted for '{0}.{1}': the call site does not fit the '{2}' shape, "
            + "so this call is not instrumented",
        category: "Qyl.AutoInstrumentation",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "The receiver type and method name match a declared qyl integration, but the signature does "
            + "not fit the shape the interceptor is generated against — typically because the library "
            + "changed the signature in a new major version. No interceptor is emitted for the call, so "
            + "it produces no qyl telemetry. Update the integration declaration or pin the library to a "
            + "version whose signature the shape describes.");
}
