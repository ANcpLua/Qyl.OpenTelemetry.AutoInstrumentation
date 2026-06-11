---
name: qyl-real-nservicebus-demo
description: Work on the real NServiceBus qyl source-interceptor demo.
---

# NServiceBus demo

Runs a real NServiceBus endpoint on the LearningTransport; no external backend. Spans come
from source-generated interceptors on `IMessageSession.Publish`/`Send`. Assembly scanning is
disabled with explicit handler registration. Managed proof only: NServiceBus 10 constructs a
Reflection.Emit proxy creator during endpoint creation, which NativeAOT structurally cannot
run — that is an NServiceBus library boundary, not a qyl gap.
