---
name: qyl-aot-verification-tools
description: Work on repo-native verification scripts for qyl AOT auto-instrumentation.
---

# tools rules

- Verification scripts should prove behavior, not self-grade prose.
- Temporary consumers are allowed when they prove package or ProjectReference behavior.
- Keep forbidden mechanism checks explicit: profiler attach, startup hooks, IL rewrite, runtime loads, reflection dispatch.
