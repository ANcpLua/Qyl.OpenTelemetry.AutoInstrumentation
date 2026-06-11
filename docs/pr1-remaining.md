# PR #1 Remaining Work

Source inventory
- PR conversation: three unresolved Codex review threads on PR #1 (HTTP/gRPC captures, conformance opt-in, smoke AOT regex).
- Repo TODO scan: no `TODO`, `FIXME`, `HACK`, or `XXX` markers in source/tools/docs content for this PR scope.
- Post-strip score/gate inventory: behavioral gate scripts already exist and are now used directly.

## Completed checklist

- [x] PR thread: keep captured HTTP headers and gRPC metadata as arrays, including single-value captures.
- [x] PR thread: do not register the conformance `ActivityListener` unless conformance is opted in.
- [x] PR thread: fix the AOT warning grep pattern in `tools/smoketest.sh`.
- [x] Gate item: keep `tools/run-hotpath-benchmarks.sh` runnable after the strip commit.
- [x] Gate item: run `bash tools/run-hotpath-benchmarks.sh` and record real output lines.
- [x] Gate item: run `python3 tools/verify-contract-invariants.py`.
- [x] Gate item: run `python3 tools/verify-contract-coverage-report.py`.
- [x] Gate item: run `python3 tools/verify-package-layout.py`.
- [x] Gate item: run `python3 tools/verify-projectreference-behavior.py`.
- [x] Gate item: run `python3 tools/verify-public-api-baseline.py`.
- [x] Gate item: run `python3 tools/verify-xml-doc-enforcement.py`.
- [x] Gate item: run `python3 tools/verify-environment-options-behavior.py`.
- [x] Gate item: run `python3 tools/verify-conformance-opt-in.py`.
- [x] Gate item: run `python3 tools/verify-generator-snapshots.py`.
- [x] Gate item: run `python3 tools/verify-source-interceptor-consumer.py`.
- [x] Gate item: run `bash tools/smoketest.sh`.
- [x] Gate item: run `python3 tools/verify-webapi-aot-demo.py`.
- [x] Gate item: run `python3 tools/verify-otlp-golden-fixtures.py`.
- [x] Gate item: run `python3 tools/verify-otlp-collector-fixtures.py`.
- [x] Gate item: run `python3 tools/verify-consumer-behavior.py`.
- [x] Gate item: run `python3 tools/verify-nativeaot-consumer-golden.py`.
- [x] Gate item: run `python3 tools/verify-aot-autoinstrumentation-goal.py`.
- [x] Release item: retarget tag `v0.3.0-pre.1` to branch HEAD and push force.

## Live output evidence

`python3 tools/verify-contract-invariants.py`

```text
contract-invariants-ok
```

`python3 tools/verify-contract-coverage-report.py`

```text
contract-coverage-report-ok total=60 source_generated_signals=33 unsupported_signals=4 environment_controls=7 instrumentation_options=16
```

`python3 tools/verify-package-layout.py`

```text
package-layout-ok
```

`python3 tools/verify-projectreference-behavior.py`

```text
projectreference-behavior-ok
```

`python3 tools/verify-public-api-baseline.py`

```text
public-api-baseline-ok
```

`python3 tools/verify-xml-doc-enforcement.py`

```text
xml-doc-enforcement-ok scope=source-generator,runtime
```

`python3 tools/verify-environment-options-behavior.py`

```text
environment-options-behavior-ok
```

`python3 tools/verify-conformance-opt-in.py`

```text
conformance-opt-in-ok
```

`python3 tools/verify-generator-snapshots.py`

```text
generator-snapshots-ok
```

`python3 tools/verify-source-interceptor-consumer.py`

```text
source-interceptor-consumer-ok
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

`python3 tools/verify-otlp-golden-fixtures.py`

```text
otlp-golden-fixtures-ok
```

`python3 tools/verify-otlp-collector-fixtures.py`

```text
Successfully created package '/tmp/qyl-otlp-collector-fixtures/feed/Qyl.AutoInstrumentation.0.3.0-pre.1.otlpcollector.1781144103285419000.nupkg'.
otlp-collector-fixtures-ok
otlp-collector-fixtures-elapsed=4.5s
```

`python3 tools/verify-consumer-behavior.py`

```text
consumer-behavior-ok
```

`python3 tools/verify-nativeaot-consumer-golden.py`

```text
nativeaot-consumer-golden-ok
```

`bash tools/run-hotpath-benchmarks.sh`

```text
Global total time: 00:03:46 (226.55 sec), executed benchmarks: 12
hotpath-benchmarks-ok artifacts=/var/folders/33/h4mz_z3x7ys2phgr3zm2wnq40000gn/T//qyl-benchmarkdotnet-artifacts
```

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
otlp-collector-fixtures-ok
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```

`git tag -f v0.3.0-pre.1 $(git rev-parse HEAD) && git push -f origin v0.3.0-pre.1`

```text
To github.com:ANcpLua/qyl-dotnet-autoinstrumentation.git
 + 4d22e94...363417e v0.3.0-pre.1 -> v0.3.0-pre.1 (forced update)
```
