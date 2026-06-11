# PR #1 Remaining Work

Source inventory:

- PR conversation: three unresolved Codex review threads on PR #1.
- Repo TODO scan: no `TODO`, `FIXME`, `HACK`, or `XXX` entries found in the current tree.
- Post-strip score/gate inventory: the deleted score ledger listed executable gates and CI evidence; the current tree keeps behavioral verifier scripts and should not restore the self-grading score document.

## Checklist

- [x] PR thread: keep captured HTTP headers and gRPC metadata as arrays, including single-value captures.
- [x] PR thread: do not register the conformance `ActivityListener` unless conformance is opted in.
- [x] PR thread: fix the AOT warning grep pattern in `tools/smoketest.sh`.
- [x] Gate item: keep `tools/run-hotpath-benchmarks.sh` runnable after the strip commit.
- [x] Gate item: run `bash tools/run-hotpath-benchmarks.sh` and record real output lines.
- [x] Gate item: run `tools/verify-contract-invariants.py`.
- [x] Gate item: run `tools/verify-contract-coverage-report.py`.
- [x] Gate item: run `tools/verify-package-layout.py`.
- [x] Gate item: run `tools/verify-projectreference-behavior.py`.
- [x] Gate item: run `tools/verify-public-api-baseline.py`.
- [x] Gate item: run `tools/verify-xml-doc-enforcement.py`.
- [x] Gate item: run `tools/verify-environment-options-behavior.py`.
- [x] Gate item: run `tools/verify-conformance-opt-in.py`.
- [x] Gate item: run `tools/verify-generator-snapshots.py`.
- [x] Gate item: run `tools/verify-source-interceptor-consumer.py`.
- [x] Gate item: run `tools/smoketest.sh`.
- [x] Gate item: run `tools/verify-webapi-aot-demo.py`.
- [x] Gate item: run `tools/verify-otlp-golden-fixtures.py`.
- [x] Gate item: run `tools/verify-otlp-collector-fixtures.py`.
- [x] Gate item: run `tools/verify-consumer-behavior.py`.
- [x] Gate item: run `tools/verify-nativeaot-consumer-golden.py`.

## Local Output Lines

`bash tools/run-hotpath-benchmarks.sh`:

```text
// ***** Found 12 benchmark(s) in total *****
| InterceptedSqlClientCommand | .NET 10.0      |  8.1351 ns | 2.0528 ns | 0.5331 ns |  8.3956 ns | 52.00 |   37.13 |         - |          NA |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 15.7230 ns | 6.8776 ns | 1.7861 ns | 16.5869 ns | 17.12 |   16.26 |         - |          NA |
| InterceptedExecuteSqlRaw | .NET 10.0      | 12.4953 ns | 11.7245 ns | 1.8144 ns | 12.7991 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | NativeAOT 10.0 | 19.3780 ns |  8.5993 ns | 2.2332 ns | 19.6846 ns |     ? |       ? |         - |           ? |
| InterceptedGetAsync | .NET 10.0      | 257.4 ns |  92.91 ns | 24.13 ns |  1.17 |    0.11 | 0.0067 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 305.0 ns | 135.25 ns | 35.13 ns |  1.11 |    0.17 | 0.0067 |     704 B |        1.00 |
Global total time: 00:04:15 (255.59 sec), executed benchmarks: 12
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```

`python3 tools/verify-aot-autoinstrumentation-goal.py`:

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
otlp-collector-fixtures-ok
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```
