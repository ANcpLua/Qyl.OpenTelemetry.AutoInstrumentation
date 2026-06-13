# Open questions ledger

Working list of unresolved design/validation questions. Every entry is either `open`,
`briefed:<B#>` (a Codex briefing exists that resolves it), or `answered:<doc/commit>`.
Update the status in the same commit that resolves the question. Linked from
[CHANGELOG.md](../CHANGELOG.md).

| # | Question | Status |
|---|---|---|
| 1 | Catalog SSOT: descriptor truth lives as C#-lines-in-Python-strings in `render_interceptor_catalog_cs` (copy machine, not generator). Where should it live in a C#-only product environment? | briefed:B1 |
| 2 | Evidence honesty: 33 `implemented` signal promises vs 4 `verified_nativeaot`. Which get promoted, which stay `compile_binding_only` deliberately, which are `unsupported`/`research`? | briefed:B0 |
| 3 | Silent non-interception: driver signature drift on a version bump means the interceptor is silently not emitted (span missing, demo gates only catch pinned versions). How does the build name the cause? | briefed:B4 |
| 4 | Options flow: `QylAutoInstrumentationOptions` is sometimes threaded as a parameter (sometimes unused) and sometimes re-read via `.Current` mid-operation. What is the one policy? | briefed:B5 |
| 5 | `QylSemConvRegistry.Contribute` is a `static partial void` whose implementation must come from `SemConvRegistryGenerator` — is that wiring actually in place for every compile that calls it? | briefed:B5 |
| 6 | What does each verifier actually prove? What do generator snapshots snapshot, against which oracle, over which request→result chain? Needs a written validation contract per tool. | briefed:B7 |
| 7 | Is `verify-source-interceptor-consumer.py` (one consumer shape) sufficient, or do special call-site shapes (extension methods, generic inference, top-level statements, global usings) need documented dedicated fixtures? | open |
| 8 | Should `verify-real-*` demos model production-shaped failures (connection refused, auth failure, driver throwing) in addition to happy paths, to prove "missing stays missing, no fallbacks"? | open |
| 9 | Mutation testing: adopt at all? If yes, which tool (e.g. Stryker.NET) and only where verifiers can't already refute a mutant? There are no `dotnet test` projects today; verifiers are end-to-end real-artifact gates. | open |
| 10 | Generator idempotence/config-soundness: what are the formal criteria that generation is deterministic, config-driven, and agnostic (the "atomic / normal form" question)? | open |
| 11 | Black-box/oracle boundary: which third-party behaviors do we trust without verification (true oracles) vs verify ourselves, to avoid false-positive-generating tests? | open |
