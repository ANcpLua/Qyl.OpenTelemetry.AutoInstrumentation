# TASK — Root-fix the double `Build()` interceptor (CS9153): delete the coordination layer

**Branch:** `claude/drop-build-interceptor-startupfilter` (off `origin/main` @ 5ceee81)
**Sibling repo:** `../qyl` (the collector consumer) — needs a matching branch/PR.
**Status:** WIP — external cut in progress.

## End goal
`Qyl.OpenTelemetry.AutoInstrumentation` no longer intercepts `WebApplicationBuilder.Build()`.
The ASP.NET Core server-span **middleware is preserved verbatim** but injected via an
`IStartupFilter` off a non-colliding seam, so it never fights ServiceDefaults for the `Build()`
call site. This deletes the entire cross-generator coordination layer (opt-out MSBuild property,
`QylInterceptedAspNetCore.Build` compose wrapper, and qyl's `IsOtelAutoInstrumentationReferenced`
reverse-check) in **both** repos. Breaking change, no back-compat → major version bump.

## Architecture decision (user-chosen)
User picked **IStartupFilter (rewire)** over the DiagnosticListener-delete option, to keep the
exact middleware span attributes (request/response header capture + query string) that the
`AspNetCoreDiagnosticListener` path does not produce. Registration is **explicit one-liner**
(`AddQylAspNetCoreInstrumentation()`) — NOT `.Hosting`, because `.Hosting`'s module-init subscribes
`AspNetCoreDiagnosticListener`, which would double-count server spans alongside the middleware.
(Fully zero-config via a builder-creation interceptor is a possible follow-up; intentionally not
built now since it would *add* generator code, against the "delete unnecessary" directive.)

## Cut manifest — external (`Qyl.OpenTelemetry.AutoInstrumentation`)
- [ ] `QylInterceptedAspNetCore.cs`: delete `Build(WebApplicationBuilder)` (keep InvokeAsync/Observe/Map*/etc.)
- [ ] ADD `QylAspNetCoreStartupFilter : IStartupFilter` (main pkg) — `app.Use(InvokeAsync)` + next
- [ ] ADD `AddQylAspNetCoreInstrumentation(IServiceCollection)` (TryAddEnumerable, idempotent)
- [ ] Generator `Descriptors.cs`: drop enum `AspNetCoreWebApplicationBuilderBuild` + dead `BuilderInitialization` shape
- [ ] Generator `Detection.cs`: drop `TryGetAspNetCoreWebApplicationBuilderBuildInvocation`
- [ ] Generator `QylGeneratedSourceInterceptorCatalog.cs`: drop the Build matcher + emission descriptor
- [ ] Generator `QylAutoInstrumentationGenerator.cs`: drop opt-out property/read + pipeline combine + EmitInterceptors filter param + BuilderInitialization arm
- [ ] `build/` + `buildTransitive/` `.targets`: drop `CompilerVisibleProperty` opt-out (keep InterceptorsNamespaces)
- [ ] Build green (0 warn); run `verify.yml`/fixtures; local pack

## Cut manifest — qyl (`../qyl`)
- [ ] `GeneratorPipelineHelpers.cs`: drop `QylInterceptedAspNetCoreTypeName` + `IsOtelAutoInstrumentationReferenced`
- [ ] `ServiceDefaultsSourceGenerator.cs`: drop otel-available pipeline/stage/field/branch → `var app = builder.Build();`
- [ ] `qyl.collector.csproj`: drop `QylAutoInstrumentationInterceptWebApplicationBuilderBuild=false` knob
- [ ] `qyl.collector/Program.cs`: add `builder.Services.AddQylAspNetCoreInstrumentation();`
- [ ] `Directory.Packages.props`: bump OTel pin to new major
- [ ] Build collector Release (0 warn); **runtime acceptance: run collector, hit endpoint, assert exactly ONE `SPAN_KIND_SERVER`**

## Release ordering (irreversible gate)
External publishes first (major, trusted publishing) → then qyl bumps pin. Do all reversible work
(edits + local pack + runtime acceptance) BEFORE the breaking publish. Surface the DiagnosticListener-
vs-IStartupFilter divergence at the publish gate.

## Invariants (do not violate)
- Generators stay `netstandard2.0`; generated code is full .NET 10. Never downgrade emitted code.
- No new public API contract models in qyl.collector (contracts single-sourced via Qyl.Api.Contracts).
