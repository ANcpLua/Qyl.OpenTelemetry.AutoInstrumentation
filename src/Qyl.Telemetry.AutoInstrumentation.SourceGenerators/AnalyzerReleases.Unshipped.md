; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------------------|----------|--------------------------------------------------------------------
QYL1001 | Qyl.AutoInstrumentation | Info | A call site naming a declared integration receiver and method does not fit the declared interceptor shape, so no interceptor is emitted and the call is not instrumented.
QYL1002 | Qyl.AutoInstrumentation | Info | A compilation uses a library whose native ActivitySource qyl subscribes but never makes the call that library needs before it emits, or before it emits in full.
