# Continuation brief

Use this file as the project scratchpad for the next senior-agent pass. Keep it factual and update
it when the project direction changes.

## Current state

- `main` is the slim five-commit history.
- The product is .NET 10 AOT-native auto-instrumentation through managed build assets,
  source generation, DiagnosticListener payloads, and module initializer boot.
- The broad verifier is `python3 tools/verify-aot-autoinstrumentation-goal.py`.
- `AGENTS.md` is the canonical agent instruction file; `CLAUDE.md` is a symlink.

## Do next

1. Build a real OTLP export normalizer and wire it into the combined verifier.
2. Add stable benchmark budgets after collecting CI variance for the current BenchmarkDotNet suite.
3. Create a contract-update tool that refreshes YAML, generated contract data, coverage matrix,
   snapshots, and docs in one command.
4. Improve consumer onboarding: package selection table, ProjectReference diagnostics, and missing
   analyzer/buildTransitive error messages.
5. Expand only source-visible, stable interceptor targets. Do not add reflection or runtime patching
   to chase hidden library internals.

## Do not regress

- Do not reintroduce profiler/startup-hook/IL-rewrite/reflection dispatch paths.
- Do not merge EFCore or SqlClient dependencies into generic Hosting.
- Do not hand-edit generated EF compiled models, snapshots, verified fixtures, or coverage matrix.
- Do not claim release/tag state without checking the tag target after history rewrites.
