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

- `QylInterceptedHttpClient`: observes supported `HttpClient` calls, records response/error/duration state, shares the synchronous `Send` path without delegate allocation, and delegates header/query formatting to core helpers.
- `QylInterceptedHttpWebRequest`: observes `HttpWebRequest` calls with the same URL-full formatting rule as `HttpClient`.
- `QylCaptureHelpers`: owns captured header/metadata tag shape and URL/query formatting helpers; captured values stay arrays even for one value.
- `QylHttpClientMetrics`: keeps the checked public metric entry point and exposes an internal unchecked path for callers that already proved recording is enabled.
- `QylAutoInstrumentationOptions`: owns instrumentation option defaults and delegates environment parsing to its nested `EnvironmentOptions` reader.
- `ModuleInitializerBoot`: owns idempotent activation and diagnostic-listener registration.
- `QylAutoInstrumentationGenerator`: maps known interceptor kinds to emitters, shares repeated activity-dispose emission, and fails fast for an unhandled kind instead of silently emitting the wrong wrapper.
- `tools/verify_helpers.py`: owns shared verifier process environment, version reading, and checked subprocess execution.
- `tools/Qyl.AutoInstrumentation.WebApiAotDemo/Program.cs`: owns the WebAPI NativeAOT demo source used by `tools/verify-webapi-aot-demo.py`.
- `tools/smoketest.sh`: owns package-reference and project-reference smoke fixtures with separate managed build, NativeAOT publish, and NativeAOT run phases.
- `tools/verify-aot-autoinstrumentation-goal.py`: owns the full handoff gate; filtered runs print a partial marker and cannot be mistaken for full release evidence.

## Live output evidence

`python3 tools/verify-aot-autoinstrumentation-goal.py`

```text
contract-invariants-ok
contract-coverage-report-ok total=60 source_generated_signals=33 unsupported_signals=4 environment_controls=7 instrumentation_options=16
Build succeeded.
0 Warning(s)
0 Error(s)
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
Successfully created package '/tmp/qyl-otlp-collector-fixtures/feed/Qyl.AutoInstrumentation.0.3.0-pre.1.otlpcollector.1781146957297871000.nupkg'.
otlp-collector-fixtures-ok
otlp-collector-fixtures-elapsed=5.6s
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
| DirectSqlClientCommand      | .NET 10.0      | 0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | .NET 10.0      | 5.0931 ns | 0.0513 ns | 0.0079 ns |     ? |       ? |         - |           ? |
| DirectSqlClientCommand      | NativeAOT 10.0 | 0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 9.0142 ns | 0.1370 ns | 0.0356 ns |     ? |       ? |         - |           ? |
| DirectExecuteSqlRaw      | .NET 10.0      |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | .NET 10.0      |  9.3700 ns | 0.9909 ns | 0.2573 ns |     ? |       ? |         - |           ? |
| DirectExecuteSqlRaw      | NativeAOT 10.0 |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | NativeAOT 10.0 | 11.5618 ns | 0.4219 ns | 0.1096 ns |     ? |       ? |         - |           ? |
| DirectGetAsync      | .NET 10.0      | 159.6 ns |  6.23 ns | 0.96 ns |  1.00 |    0.01 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 165.3 ns |  7.15 ns | 1.86 ns |  1.04 |    0.01 | 0.0069 |     704 B |        1.00 |
| DirectGetAsync      | NativeAOT 10.0 | 205.1 ns | 26.48 ns | 6.88 ns |  1.00 |    0.04 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 197.3 ns |  9.93 ns | 1.54 ns |  0.96 |    0.03 | 0.0069 |     704 B |        1.00 |
Global total time: 00:04:21 (261.2 sec), executed benchmarks: 12
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```
