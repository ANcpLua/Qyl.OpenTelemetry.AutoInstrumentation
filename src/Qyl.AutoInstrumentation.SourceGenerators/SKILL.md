---
name: qyl-source-generators
description: Work on the netstandard2.0 Roslyn generator that emits qyl source interceptors and contract registries.
---

# Source generator

This project is build-time only. It may reference Roslyn APIs but must remain isolated from runtime publish. Generated output must be deterministic and verified by snapshots.
