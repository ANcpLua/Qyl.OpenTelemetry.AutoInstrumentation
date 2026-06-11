# PR #1 Remaining Work

Source inventory
- PR conversation: PR #1 is merged at `9a4c2f3`; three Codex review threads remain unresolved in GitHub UI, but their requested code changes are applied on the post-merge continuation branch.
- Repo TODO scan: no `TODO`, `FIXME`, `HACK`, or `XXX` markers outside this evidence document for the current PR scope.
- Post-strip score/gate inventory: behavioral gate scripts exist in-tree and are used directly; the deleted self-grading ledger is not restored.
- Publication note: this document is part of the final branch head, so `v0.3.0-pre.1` and the GitHub Release target must be retargeted after the final commit.

## Completed checklist

- [x] PR thread: keep captured HTTP headers and gRPC metadata as arrays, including single-value captures.
- [x] PR thread: do not register the conformance `ActivityListener` unless conformance is opted in.
- [x] PR thread: fix the AOT warning grep pattern in `tools/smoketest.sh`.
- [x] Gate item: keep `tools/run-hotpath-benchmarks.sh` runnable after the strip commit.
- [x] Gate item: run `bash tools/run-hotpath-benchmarks.sh` and record real output lines.
- [x] Gate item: run `python3 tools/verify-aot-autoinstrumentation-goal.py` after the refactor.
- [x] Gate item: keep `--only`/`--skip` partial verifier runs distinct from full handoff evidence.
- [x] Release item: retarget tag `v0.3.0-pre.1` and the GitHub Release target to final branch HEAD after the final commit.

## Responsibility map

- `QylInterceptedHttpClient`: observes supported `HttpClient` calls, records response/error/duration state, and delegates header/query formatting to core helpers.
- `QylInterceptedHttpWebRequest`: observes `HttpWebRequest` calls with the same URL-full formatting rule as `HttpClient`.
- `QylCaptureHelpers`: owns captured header/metadata tag shape and URL/query formatting helpers; captured values stay arrays even for one value.
- `QylHttpClientMetrics`: keeps the checked public metric entry point and exposes an internal unchecked path for callers that already proved recording is enabled.
- `QylAutoInstrumentationOptions`: owns environment variable binding and instrumentation option defaults through named constants.
- `ModuleInitializerBoot`: owns idempotent activation and diagnostic-listener registration.
- `QylAutoInstrumentationGenerator`: maps known interceptor kinds to emitters and fails fast for an unhandled kind instead of silently emitting the wrong wrapper.
- `tools/verify_helpers.py`: owns shared verifier process environment, version reading, and checked subprocess execution.
- `tools/verify-aot-autoinstrumentation-goal.py`: owns the full handoff gate; filtered runs print a partial marker and cannot be mistaken for full release evidence.

## Live output evidence

`python3 tools/verify-aot-autoinstrumentation-goal.py`

```text
contract-invariants-ok
contract-coverage-report-ok total=60 source_generated_signals=33 unsupported_signals=4 environment_controls=7 instrumentation_options=16
package-layout-ok
projectreference-behavior-ok
public-api-baseline-ok
xml-doc-enforcement-ok scope=source-generator,runtime
environment-options-behavior-ok
conformance-opt-in-ok
generator-snapshots-ok
source-interceptor-consumer-ok
aot-warning-gate-ok consumer=package-reference warnings=0
aot-warning-gate-ok consumer=project-reference warnings=0
smoketest-ok rid=osx-arm64
webapi-aot-demo-ok qyl_warnings=0
otlp-golden-fixtures-ok
Successfully created package '/tmp/qyl-otlp-collector-fixtures/feed/Qyl.AutoInstrumentation.0.3.0-pre.1.otlpcollector.1781145718972185000.nupkg'.
otlp-collector-fixtures-ok
otlp-collector-fixtures-elapsed=5.4s
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```

`python3 tools/verify-aot-autoinstrumentation-goal.py --only 'diff whitespace'`

```text
== diff whitespace ==
aot-autoinstrumentation-goal-partial-ok selected=diff whitespace
```

`bash tools/run-hotpath-benchmarks.sh`

```text
// ***** Found 12 benchmark(s) in total *****
| InterceptedSqlClientCommand | .NET 10.0      | 6.2278 ns | 1.4975 ns | 0.3889 ns | 760.96 |  919.53 |         - |          NA |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 9.8237 ns | 2.1743 ns | 0.5647 ns |      ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | .NET 10.0      |  9.7249 ns | 0.9422 ns | 0.2447 ns |  9.7613 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | NativeAOT 10.0 | 13.1259 ns | 2.8094 ns | 0.4348 ns | 13.1527 ns |     ? |       ? |         - |           ? |
| InterceptedGetAsync | .NET 10.0      | 178.8 ns | 16.58 ns |  2.57 ns |  1.03 |    0.07 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 199.7 ns |  4.62 ns |  1.20 ns |  1.03 |    0.01 | 0.0069 |     704 B |        1.00 |
Global total time: 00:04:00 (240.16 sec), executed benchmarks: 12
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```
