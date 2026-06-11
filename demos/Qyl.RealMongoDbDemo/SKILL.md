---
name: qyl-real-mongodb-demo
description: Work on the real MongoDB.Driver qyl source-interceptor demo.
---

# MongoDB demo

Requires a real MongoDB endpoint for full proof. Command spans come from source-generated
interceptors on `IMongoCollection<T>` methods. Use `BsonDocument` payloads to keep the
driver's reflection serializer surface out of the demo.
