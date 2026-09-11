# Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners

The base `DiagnosticListener` subscriber and the shared semantics helpers that qyl's
`DiagnosticSource`-based instrumentations build on. It is AOT-native: a subscription over public
`DiagnosticListener` events, with no IL rewriting and no profiler.

This package is a dependency of
[`Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore`](https://www.nuget.org/packages/Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore)
and
[`Qyl.Telemetry.AutoInstrumentation.SqlClient`](https://www.nuget.org/packages/Qyl.Telemetry.AutoInstrumentation.SqlClient),
and arrives through them. An application references it directly only to build its own
`DiagnosticListener` instrumentation on the same base:

```bash
dotnet add package Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners
```

Every attribute the helpers write is a generated constant from
[`Qyl.Telemetry.SemanticConventions`](https://www.nuget.org/packages/Qyl.Telemetry.SemanticConventions).

## More

- How a library gets instrumented:
  [repository README](https://github.com/ANcpLua/Qyl.OpenTelemetry.AutoInstrumentation#how-a-library-gets-instrumented)
- Changes per version:
  [CHANGELOG](https://github.com/ANcpLua/Qyl.OpenTelemetry.AutoInstrumentation/blob/main/CHANGELOG.md)
