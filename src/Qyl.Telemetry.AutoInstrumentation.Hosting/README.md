# Qyl.Telemetry.AutoInstrumentation.Hosting

Process bootstrap for qyl's automatic instrumentation. A `[ModuleInitializer]` activates the qyl
listeners when the assembly loads, and `AddQylAutoInstrumentation()` wires them explicitly for
applications that prefer a visible call.

Most applications get this package through
[`Qyl.Telemetry.Hosting`](https://www.nuget.org/packages/Qyl.Telemetry.Hosting) and its one-line
`builder.AddQyl()`, and never reference it directly.

Reference it on its own when the application owns its OpenTelemetry SDK setup and only needs qyl's
listeners activated:

```bash
dotnet add package Qyl.Telemetry.AutoInstrumentation.Hosting
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-service"))
    .WithTracing(t => t
        .AddSource("Qyl.Telemetry.AutoInstrumentation")
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
```

The instrumentation packages ship no exporter of their own. An application that also subscribes a
native source qyl already covers, such as `System.Net.Http`, exports that operation twice.

Publishing with `-p:PublishAot=true` produces no trim, AOT or analyzer warning from this package.

## More

- The instrumented libraries and the qyl attributes:
  [repository README](https://github.com/ANcpLua/Qyl.OpenTelemetry.AutoInstrumentation#readme)
- Changes per version:
  [CHANGELOG](https://github.com/ANcpLua/Qyl.OpenTelemetry.AutoInstrumentation/blob/main/CHANGELOG.md)
