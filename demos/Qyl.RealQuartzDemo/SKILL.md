---
name: qyl-real-quartz-demo
description: Work on the real Quartz qyl source-interceptor demo.
---

# Quartz demo

Runs a real in-process Quartz scheduler; no external backend. Spans come from source-visible
`IJob.Execute` delegation calls inside the scheduler-fired job. Scheduler-internal job dispatch
is compiled library code and is not reachable by source interception — only source-visible
`Execute` composition calls are. Keep that boundary explicit.
