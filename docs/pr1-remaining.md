# PR #1 Remaining Work

Source inventory:

- PR conversation: three unresolved Codex review threads on PR #1.
- Repo TODO scan: no `TODO`, `FIXME`, `HACK`, or `XXX` entries found in the current tree.
- Post-strip score/gate inventory: the deleted score ledger listed executable gates and CI evidence; the current tree keeps behavioral verifier scripts and does not reintroduce the self-grading score document.

Checklist

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

## Live output evidence

`bash tools/run-hotpath-benchmarks.sh`

```text
// Found 4 benchmarks:
//   HttpClientHotPathBenchmarks.DirectGetAsync: Job-OIKEGS(Runtime=.NET 10.0, IterationCount=5, LaunchCount=1, WarmupCount=3)
//   HttpClientHotPathBenchmarks.InterceptedGetAsync: Job-OIKEGS(Runtime=.NET 10.0, IterationCount=5, LaunchCount=1, WarmupCount=3)
//   HttpClientHotPathBenchmarks.DirectGetAsync: Job-GYYQXO(Runtime=NativeAOT 10.0, IterationCount=5, LaunchCount=1, WarmupCount=3)
//   HttpClientHotPathBenchmarks.InterceptedGetAsync: Job-GYYQXO(Runtime=NativeAOT 10.0, IterationCount=5, LaunchCount=1, WarmupCount=3)
// ...
// * Summary *
| Method              | Runtime        | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------- |--------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| DirectGetAsync      | .NET 10.0      | 235.1 ns | 70.80 ns | 18.39 ns |  1.01 |    0.10 | 0.0067 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 225.1 ns | 82.36 ns | 21.39 ns |  0.96 |    0.11 | 0.0067 |     704 B |        1.00 |
| DirectGetAsync      | NativeAOT 10.0 | 257.4 ns | 61.90 ns |  9.58 ns |  1.00 |    0.05 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 302.7 ns | 87.88 ns | 22.82 ns |  1.18 |    0.09 | 0.0067 |     704 B |        1.00 |
Global total time: 00:04:22 (262 sec), executed benchmarks: 12
// * Artifacts cleanup *
Artifacts cleanup is finished
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```

`python3 tools/verify-aot-autoinstrumentation-goal.py` (and all component verifiers)

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
otlp-collector-fixtures-ok
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```

`bash tools/smoketest.sh`

```text
aot-warning-gate-ok consumer=package-reference warnings=0
aot-warning-gate-ok consumer=project-reference warnings=0
smoketest-ok rid=osx-arm64
```

`python3 tools/verify-webapi-aot-demo.py`

```text
webapi-aot-demo-ok qyl_warnings=0
```

`python3 tools/verify-otlp-collector-fixtures.py`

```text
  Successfully created package '/tmp/qyl-otlp-collector-fixtures/feed/Qyl.AutoInstrumentation.0.3.0-pre.1.otlpcollector.1781142143822741000.nupkg'.
otlp-collector-fixtures-ok
otlp-collector-fixtures-elapsed=6.1s
```

`python3 tools/verify-consumer-behavior.py`

```text
consumer-behavior-ok
```

`python3 tools/verify-nativeaot-consumer-golden.py`

```text
nativeaot-consumer-golden-ok
```

`git tag -f v0.3.0-pre.1 $(git rev-parse HEAD) && git push -f origin v0.3.0-pre.1`

```text
Everything up-to-date
```
