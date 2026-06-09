---
name: qyl-aot-benchmarks
description: Work on BenchmarkDotNet measurement projects for qyl hot-path cost and allocation evidence.
---

# benchmark rules

- Benchmarks are measurement evidence, not shipped product code.
- Keep no-listener and baseline-matched allocation scenarios separate.
- Do not convert benchmark results into hard gates until CI variance is measured.
