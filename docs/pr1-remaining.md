# PR #1 Remaining Work

Source inventory
- PR conversation: PR #1 is merged at `9a4c2f3`; three Codex review threads remain unresolved in GitHub UI, but their requested code changes are applied on the post-merge continuation branch.
- Repo TODO scan: no `TODO`, `FIXME`, `HACK`, or `XXX` markers outside this evidence document for the current PR scope.
- Post-strip score/gate inventory: behavioral gate scripts exist in-tree and are used directly; the deleted self-grading score document is not restored. `COVERAGE_LEDGER.md` remains the tracked product coverage ledger.
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

- `QylInterceptedHttpClient`: observes supported `HttpClient` calls, records effective URI, default/content request headers, response/error/status/duration state, shares the synchronous `Send` path without delegate allocation, and delegates header/query formatting to core helpers.
- `QylInterceptedHttpWebRequest`: observes `HttpWebRequest` calls with the same URL-full formatting rule as `HttpClient`.
- `QylCaptureHelpers`: owns captured header/metadata tag shape and URL/query formatting helpers; captured values stay arrays even for one value.
- `QylHttpClientMetrics`: keeps the checked public metric entry point and exposes an internal unchecked path for callers that already proved recording is enabled.
- `QylAutoInstrumentationOptions`: owns instrumentation option defaults and delegates environment parsing to its nested `EnvironmentOptions` reader.
- `ModuleInitializerBoot`: owns idempotent activation and subscribes the explicit diagnostic-listener registry.
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
Successfully created package '/tmp/qyl-otlp-collector-fixtures/feed/Qyl.AutoInstrumentation.0.3.0-pre.1.otlpcollector.1781150195632936000.nupkg'.
otlp-collector-fixtures-ok
otlp-collector-fixtures-elapsed=4.9s
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```

`python3 tools/verify-aot-autoinstrumentation-goal.py --only 'diff whitespace'`

```text
== diff whitespace ==
aot-autoinstrumentation-goal-partial-ok selected=diff whitespace
```

`python3 tools/verify-aot-autoinstrumentation-goal.py --only 'contract invariants,diff whitespace' --skip 'contract invariants'`

```text
== diff whitespace ==
aot-autoinstrumentation-goal-partial-ok selected=diff whitespace
```

`bash tools/run-hotpath-benchmarks.sh`

```text
// ***** Found 12 benchmark(s) in total *****
| DirectSqlClientCommand      | .NET 10.0      | 0.1380 ns | 0.4785 ns | 0.1243 ns |     ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | .NET 10.0      | 5.8820 ns | 1.0565 ns | 0.1635 ns |     ? |       ? |         - |           ? |
| DirectSqlClientCommand      | NativeAOT 10.0 | 0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 9.5546 ns | 1.0325 ns | 0.1598 ns |     ? |       ? |         - |           ? |
| DirectExecuteSqlRaw      | .NET 10.0      |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | .NET 10.0      |  8.8009 ns | 0.0577 ns | 0.0150 ns |     ? |       ? |         - |           ? |
| DirectExecuteSqlRaw      | NativeAOT 10.0 |  0.0176 ns | 0.1082 ns | 0.0281 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | NativeAOT 10.0 | 12.1969 ns | 0.5800 ns | 0.0898 ns |     ? |       ? |         - |           ? |
| DirectGetAsync      | .NET 10.0      | 166.7 ns |  24.76 ns |  6.43 ns |  1.00 |    0.05 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 167.5 ns |   3.28 ns |  0.51 ns |  1.01 |    0.03 | 0.0069 |     704 B |        1.00 |
| DirectGetAsync      | NativeAOT 10.0 | 199.8 ns |  17.58 ns |  2.72 ns |  1.00 |    0.02 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 227.2 ns | 185.02 ns | 28.63 ns |  1.14 |    0.13 | 0.0069 |     704 B |        1.00 |
Global total time: 00:04:04 (244.16 sec), executed benchmarks: 12
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```
