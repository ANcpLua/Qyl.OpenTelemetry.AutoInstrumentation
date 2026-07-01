# TASK — AOT-publish CI gate

> Separate file on purpose: `.claude/TASK.md` holds a different WIP (double-`Build()` interceptor /
> IStartupFilter rewire on `claude/drop-build-interceptor-startupfilter`) and must not be clobbered.

**Branch:** `feat/aot-publish-gate` (off clean `main`).

## End goal
Keep qyl's NativeAOT claims honest automatically: a CI gate that pins which demos AOT-publish
**warning-clean** vs which publish with **tolerated third-party trim/AOT warnings**, and fails on
regression / hard-break / silent classification drift.

## Findings (2026-07)
Full solution build green; qyl substrate AOT-publishes clean; runtime interception verified
(RequestDelegate + ILogger). Forced `PublishAot` sweep of all 30 demos under strict
`TreatWarningsAsErrors`: **17 warning-clean, 13 vendor-warned** — the 13 emit `IL2104/IL3053/IL3002`
from **third-party assemblies only** (zero `Qyl.*` warn). Not broken: `verify-real-*-demo.py`
publish them with `-p:TreatWarningsAsErrors=false` and runtime-verify via testcontainers.

## Checklist
- [x] Classify 30 demos (17 clean / 13 vendor-warned + warning source).
- [x] `tools/verify-aot-publish-gate.py` (regression / hard-break / promotion; markdown summary).
- [x] `.github/workflows/aot-publish-gate.yml` (PR: `--set clean`; push: also `--set warned`).
- [x] Local validation on a clean + a warned demo.
- [ ] PR opened; CI green.

## Related PR
PR #26 (`fix/dbcontract-default-throw-and-dead-field`): GetDbTraceContractKey default→throw;
ReceiverTypePattern surfaced in the Telemetry Capability Graph. Independent of this gate.
