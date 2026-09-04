; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------------------|----------|--------------------------------------------------------------------
QYL1001 | Qyl.AutoInstrumentation | Info | A call site naming a declared integration receiver and method does not fit the declared interceptor shape, so no interceptor is emitted and the call is not instrumented.
