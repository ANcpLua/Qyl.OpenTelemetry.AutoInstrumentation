# Task: Descriptor-Metadata Root-Fix — delete self-referential validation via type-system refactor

**Branch:** `refactor/descriptor-metadata-root-fix` (off `main` @ a70356b)
**Started:** 2026-07-02
**Status:** WIP
**Goal:** Remove the generator's self-referential validation apparatus (~300 lines) by making the
invariants structural instead of runtime-checked. Generated output must stay **byte-identical**
(snapshot test is the gate).

## Evidence (verified 2026-07-01/02)

Every read of these members is in validation code — zero functional use:
- `InterceptorMatcherDescriptor.TargetKindMask` (3 ctor writes, 3 validator reads)
- `InterceptorMatcherDescriptor.ContractKeys` (1 read: EnsureContractDeclaredByMatcher)
- `InterceptorMatcherDescriptor.Family/.MethodShape` (copied to target, compared in validator)
- `InterceptorTarget.MatcherName/.MatcherFamily/.MatcherMethodShape` (validation payload)
- `InterceptorEmissionDescriptor.Family/.MethodShape/.SignalOwnership/.ErrorPolicy/.DurationPolicy`
  (ValidateEmissionDescriptorPolicy itself proves policies are fully determined by body type)
- `GetDbTraceContractKey` == `"signals.traces." + id` for all 7 ids GetDbInstrumentationId returns

`tools/verify-contract-invariants.py` pins the validators as required tokens (lines ~1709-1721)
and parses the policy enums as data — those checks verify consistency between two representations
of the same fact; when one representation is deleted, they become obsolete or get rewritten to
the single surviving source.

## Plan / Checklist

- [x] Evidence pass (all field reads mapped; python harness dependencies mapped)
- [x] Branch created
- [ ] **Cut 1 — Descriptors.cs:** body-descriptor hierarchy
      (`abstract record InterceptorBodyDescriptor` + 8 sealed records, drop `IsDefined` on the 8;
      `InterceptorEmissionDescriptor(Kind, Body)`; matcher: 8 ctors → 2, drop mask/keys/family/shape;
      target: drop 3 Matcher* fields; delete enums EmitterFamily/MethodShape/SignalOwnership/
      ErrorPolicy/DurationPolicy)
- [ ] **Cut 2 — QylGeneratedSourceInterceptorCatalog.cs:** rewrite 25 matcher rows + 29 emission rows
      to the slim constructors; keep GetInterceptorReceiverSurface (TCG consumer)
- [ ] **Cut 3 — QylAutoInstrumentationGenerator.cs:** delete ValidateDescriptorCatalog + Initialize()
      call, Ensure* trio, EnsureEmissionDescriptorMatchesMatcher, ValidateEmissionDescriptorPolicy,
      ValidateSingleBodyDescriptor, ValidateMethodShape ×2, ValidatePolicy; dispatch = type switch on
      `descriptor.Body`; keep GetEmissionDescriptor terminal throw (functional lookup failure)
- [ ] **Cut 4 — Shapes.cs/Detection.cs:** delete GetDbTraceContractKey (inline `"signals.traces." + id`),
      delete InterceptorKinds()/GetInterceptorKindMask (mask machinery)
- [ ] **Cut 5 — tools/verify-contract-invariants.py:** rewrite affected checks — keep real invariants
      (kind completeness + uniqueness in emission catalog, contract-key coverage; derive DB trace keys
      from GetDbInstrumentationId), delete redundant-representation checks (ownership consistency,
      matcher-declared kinds/keys, required-token pins of deleted validators)
- [ ] Build full solution green (TWAE=true)
- [ ] `tools/verify-generator-snapshots.py` green (byte-identical output)
- [ ] `tools/verify-contract-invariants.py` green
- [ ] Smoke: AspNetCore + ILogger runtime verifiers (same gate the last generator commit used)
- [ ] Commit increments, push, PR, CodeRabbit loop, merge on green

## Non-goals
- Keep `GetInterceptorReceiverSurface` / TCG `interceptorReceivers` section (now a consumed feature).
- Keep nested optional helper structs' `IsDefined` pattern (RuntimeHelper, DurationMetric, etc.).
- No change to InstrumentationContract counters (verified in sync; separate concern).

## Resume notes
If interrupted: all design decisions are in this file; the snapshot test defines correctness.
Verify scripts live in `tools/`. Do not re-add the policy enums — they are derived data.
