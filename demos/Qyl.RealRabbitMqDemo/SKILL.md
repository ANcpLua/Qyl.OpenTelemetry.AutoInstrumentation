---
name: qyl-real-rabbitmq-demo
description: Work on the real RabbitMQ.Client qyl source-interceptor demo.
---

# RabbitMQ demo

Requires a real RabbitMQ broker endpoint for full proof. Publish spans come from
source-generated interceptors on `IChannel.BasicPublishAsync`; publisher confirmations are
enabled so a missing exchange fails the awaited publish deterministically.
