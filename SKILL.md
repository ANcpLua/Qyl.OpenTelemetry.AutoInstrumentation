---
name: qyl-aot-autoinstrumentation
description: Work in qyl-dotnet-autoinstrumentation, the .NET 10 NativeAOT managed auto-instrumentation runtime. Use for runtime source interceptors, DiagnosticListener payload extraction, package build assets, AOT verification, and repo handoff work.
---

# qyl AOT auto-instrumentation

Use this skill for this repository only.

- Keep the lane runtime/AOT auto-instrumentation.
- Do not mix in semconv package generation or the old profiler substrate.
- Preserve the no-profiler, no-startup-hook, no-runtime-IL-rewrite, no-reflection-dispatch rule.
- Prefer package-reference zero-code behavior and explicit ProjectReference dogfooding.
- Validate handoffs with `python3 tools/verify-aot-autoinstrumentation-goal.py`.
