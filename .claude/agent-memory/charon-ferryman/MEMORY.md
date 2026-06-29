# Charon Ferryman — Memory Index

- [slnx-green hides analyzer breakage](feedback_slnx-green-hides-analyzer-breakage.md) — a clean `dotnet build slnx` can hide an unconsumable generator package; run smoketest/verify to catch CS9057
- [Precompilation experiment leaked into production generators](project_precompilation-leaked-into-production.md) — PR #12 wired nightly-only RegisterPreCompilationSourceOutput into the prod SourceGenerators, breaking consumers
