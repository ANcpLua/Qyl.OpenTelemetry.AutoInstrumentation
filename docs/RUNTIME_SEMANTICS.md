# Runtime Semantics

This is the runtime auto-instrumentation lane. Semantic-convention package generation is a
separate concern.

The agent must find values at runtime from the instrumented library's own APIs, DiagnosticSource
events, Activity tags, or error paths. Zero-code means the app does not call qyl boot APIs or add
instrumentation code; qyl code lives in the package bootstrap and listeners.

## Global Rules

- Emit stable OpenTelemetry attributes by default.
- Consume deprecated aliases as input only; do not re-emit deprecated keys.
- Do not invent values. Missing runtime values stay missing.
- Keep raw sensitive values gated off by default.
- Keep span names bounded. Do not include full URLs, paths, query text, IDs, or exception messages.
- Prefer low-cardinality summaries: route templates over paths, database operation/summary over raw
  SQL, and well-known error identifiers over exception messages.

## Privacy

Default-off sensitive attributes:

| Attribute | Reason |
|---|---|
| `url.full` | Can contain query secrets and user identifiers. |
| `url.path` | Can contain user/resource IDs when no route template is available. |
| `db.query.text` | Can contain literals, secrets, and high-cardinality statements. |

Set `QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES=true` to emit these raw values.

## Library Matrix

| Library | Runtime source | Event/error path knowledge | Emitted stable defaults | Status |
|---|---|---|---|---|
| `System.Net.Http.HttpClient` | `HttpHandlerDiagnosticListener`; `Activity.Current` tags on `System.Net.Http.HttpRequestOut.Stop`. | Real local .NET 10 proof covers 503 response and connection failure. Error values observed: status-code string such as `503`, and BCL low-cardinality `connection_error`. | `http.request.method`, `server.address`, `server.port`, `http.response.status_code`, `error.type`. | Real managed + NativeAOT proof. |
| ASP.NET Core | `Microsoft.AspNetCore` listener; `HttpContext` payload on `Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop`. | Real local .NET 10 Kestrel proof covers 204 route response and unhandled-exception 500 response. Route source is `RouteEndpoint.RoutePattern.RawText`; `Request.Path` remains privacy-gated. | `http.request.method`, `http.route`, `http.response.status_code`, `error.type` for 5xx. | Real managed + NativeAOT proof. |
| EFCore | `Microsoft.EntityFrameworkCore` listener; current implementation consumes command event aliases. | Real .NET 10 Sqlite probe observed typed `CommandExecutedEventData` and `CommandErrorEventData` payloads with `Command`, `CommandSource`, `Context`, `ExecuteMethod`, `Duration`, and exception type. Plain NativeAOT failed at runtime because EFCore model building is not supported; the compiled-model `dotnet ef dbcontext optimize --nativeaot` path published and ran. | `db.system`, `db.namespace`, `db.operation.name`, `db.query.summary`; `db.query.text` privacy-gated. | Real payload/AOT prerequisite proven; qyl listener still synthetic-only until the typed EFCore reader is isolated from the shared hosting dependency graph. |
| SqlClient | `SqlClientDiagnosticListener`; current implementation consumes command event aliases. | Microsoft.Data.SqlClient/System.Data.SqlClient command/error real payload matrix still pending. | `db.system=microsoft.sql_server`, `db.namespace`, `db.operation.name`, `db.query.summary`, `server.address`; `db.query.text` privacy-gated. | Synthetic qyl proof only. |
| Grpc.Net.Client | `Grpc.Net.Client` listener; current implementation consumes gRPC aliases. | Real client success/cancel/status code matrix still pending. | `rpc.system=grpc`, `rpc.service`, `rpc.method`, `server.address`, `server.port`, `rpc.grpc.status_code`, `error.type`. | Synthetic qyl proof only. |

## Evidence Commands

Real HttpClient, project-reference bootstrap simulation:

```bash
dotnet run --project demos/Qyl.RealHttpClientDemo/Qyl.RealHttpClientDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealHttpClientDemo/Qyl.RealHttpClientDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-httpclient-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-real-httpclient-aot/Qyl.RealHttpClientDemo
```

Real ASP.NET Core, project-reference bootstrap simulation:

```bash
dotnet run --project demos/Qyl.RealAspNetCoreDemo/Qyl.RealAspNetCoreDemo.csproj -c Release --no-build --no-launch-profile
dotnet publish demos/Qyl.RealAspNetCoreDemo/Qyl.RealAspNetCoreDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-aspnetcore-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-real-aspnetcore-aot/Qyl.RealAspNetCoreDemo
```

EFCore NativeAOT finding:

```bash
dotnet publish /tmp/qyl-efcore-ds-probe/Qyl.EfCoreDsProbe.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:SelfContained=true -o /tmp/qyl-efcore-ds-probe/aot
/tmp/qyl-efcore-ds-probe/aot/Qyl.EfCoreDsProbe
# fails: Model building is not supported when publishing with NativeAOT. Use a compiled model.

dotnet ef dbcontext optimize --project /tmp/qyl-efcore-ds-probe/Qyl.EfCoreDsProbe.csproj --startup-project /tmp/qyl-efcore-ds-probe/Qyl.EfCoreDsProbe.csproj --context Qyl.EfCoreDsProbe.ProbeContext --output-dir CompiledModels --namespace Qyl.EfCoreDsProbe.CompiledModels --nativeaot
dotnet publish /tmp/qyl-efcore-ds-probe/Qyl.EfCoreDsProbe.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:SelfContained=true -p:DefineConstants=USE_COMPILED_MODEL -o /tmp/qyl-efcore-ds-probe/aot-compiled
/tmp/qyl-efcore-ds-probe/aot-compiled/Qyl.EfCoreDsProbe
# passes, but EFCore still emits package-level trim/AOT warnings.
```

Synthetic multi-domain semantic proof:

```bash
dotnet run --project demos/Qyl.LiveInstrumentationDemo/Qyl.LiveInstrumentationDemo.csproj -c Release --no-build -- --json /tmp/qyl-live-semantics.json --html /tmp/qyl-live-semantics.html
dotnet publish demos/Qyl.LiveInstrumentationDemo/Qyl.LiveInstrumentationDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-live-semantics-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-live-semantics-aot/Qyl.LiveInstrumentationDemo --json /tmp/qyl-live-semantics-aot.json --html /tmp/qyl-live-semantics-aot.html
```
