---
name: qyl-real-redis-demo
description: Work on the real StackExchange.Redis qyl source-interceptor demo.
---

# Redis demo

Requires a real Redis endpoint for full proof. Command spans come from source-generated
interceptors on `IDatabaseAsync` commands; `ExecuteAsync` with an unknown command proves the
error path deterministically.
