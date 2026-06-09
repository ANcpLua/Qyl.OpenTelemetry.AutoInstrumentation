---
name: qyl-aot-runtime-src
description: Work on product source under src for the qyl .NET 10 NativeAOT auto-instrumentation runtime.
---

# src rules

- Runtime projects must stay trim/AOT/single-file analyzer clean.
- Source generators are build-time only and must not enter NativeAOT publish graphs.
- Keep EFCore and SqlClient integrations in their package-specific projects.
- Do not hand-edit generated build assets; fix the source generator or MSBuild inputs.
