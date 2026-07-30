---
name: grounding-audit
description: Sweep a repository for poisoned grounding — claims ABOUT the code (doc comments, examples, README API sketches, test assertions on prose, CHANGELOG summaries) that the code never satisfied or has drifted from. Verify every claim against actual behavior, classify it (true / lie / stale / unverifiable), fix docs to match reality, and restructure so the lie cannot recur. Use when something "feels wrong but you can't explain why", when a test asserts wording instead of structure, when a doc example looks slightly off, or after any refactor that changed observable behavior.
license: MIT
metadata:
  author: Alex + Claude Fable 5
  version: "1.0.0"
  origin: dotcov sweep, 2026-07-30 — a ToString doc-example showed a format the method never produced
---

# Grounding Audit

You are auditing **claims about the code, not the code itself**. A repository contains two
kinds of text: the code (which does what it does) and assertions *about* the code — doc
comments, inline examples, README API sketches, usage snippets, test assertions on prose,
CHANGELOG claims, architecture notes. When an assertion is false, every reader builds on it:
humans trust it, AI agents ingest it as ground truth, tests get written against it, and the
error compounds downstream. That is not a code smell — it is **poisoned grounding**: the
reasoning that follows from it is corrupted even when each step is locally sound.

The founding case: a `ToString()` xml-doc example showed `FAIL: line 62.0% < 80% (...)` — a
format the method **never produced** in any version. Nothing was broken, nothing failed, and
every consumer of that doc was wrong.

## When this fires

- The "Bauchgefühl" trigger: something reads plausible but you cannot verify it from memory.
  Plausible-but-unverified in a doc is exactly what a lie looks like.
- A test asserts prose (`Assert.Contains("line coverage below", ...)`) instead of structure.
- A doc example's output/shape looks *slightly* off from what the code suggests.
- After a refactor or rename: every claim about the touched surface is now suspect.
- Coverage or a reviewer flags a branch that documentation says is impossible (or misses one
  documentation says exists).

## Procedure

### 1. Inventory the claims

Collect every assertion about behavior in scope. Sources, in descending blast radius:

1. README / docs: API sketches, usage examples, feature bullets, quoted outputs.
2. Doc comments: `<summary>`, `<remarks>`, `<example>` — **especially literal example output**.
3. Test assertions that encode wording, formats, or example values.
4. Inline comments that explain *why* or claim *what happens* ("this never throws", "callers
   always pass absolute paths").
5. CHANGELOG / commit-message claims that describe current state (history entries stay as
   history — never rewrite the log).

### 2. Verify each claim against reality

The code is the measurement instrument; the claim is the hypothesis.

- Read the implementation the claim describes. Do not trust adjacency — a comment three lines
  above a method is not evidence about that method.
- For claimed outputs (doc examples, README snippets): **execute or mentally trace the exact
  code path** and compare character-for-character. Format strings, culture, ordering, and
  punctuation are where lies hide.
- For "consumer" claims ("callers parse X", "CI depends on Y"): grep for the actual consumers
  and check what they really do. The scariest lies are about who depends on what.
- Environment-dependent behavior (locale, OS, timezone) counts as a claim: output documented
  as `62.0%` is a lie on a de-AT host if formatting is culture-sensitive.

### 3. Classify

| Verdict | Meaning | Action |
|---|---|---|
| **TRUE** | Claim matches behavior | Leave it; optionally pin it with a test |
| **LIE** | Never was true in any reachable version | Fix immediately; find what was built on it |
| **STALE** | Was true, drifted after a change | Fix; note which change orphaned it |
| **UNVERIFIABLE** | Cannot be checked from the repo | Rewrite so it is checkable, or delete it |

For every LIE and STALE, trace the **poisoning chain**: what else (tests, docs, consumer
code, past decisions) was written by someone who believed it? Those are new audit targets.

### 4. Fix — with direction rules

- **Code wins by default.** Docs are corrected to describe what the code does.
- **Exception:** when the claim documents *intent* and the code deviates from it in a way
  nobody chose, the code is the bug. Decide explicitly; say which way you ruled and why.
- **Restructure so the lie cannot recur:**
  - Prose asserted in tests → replace with structured assertions (enums, computed booleans);
    pin the canonical wording in **exactly one golden test**, so rewording is a one-test
    change instead of a scatter of false failures.
  - Consumers parsing human-readable output → give them a structured API and document
    "branch on these, never parse the prose".
  - Environment-sensitive output documented as fixed → make the output invariant, and pin it
    with a test that runs under a hostile locale/environment.
  - Doc examples → regenerate from actual execution, never from memory.

### 5. Report the sweep verdict

End with a short verdict in this shape (it is the deliverable):

> **Sweep result:** logic clean / N lies / M stale. Consumers X and Y never depended on the
> false claim (verified by …). The poisoning was confined to {tests, docs}; fixed by {…};
> recurrence prevented by {golden test / structured API / invariant output}.

Name what was **checked and found clean** as explicitly as what was found wrong — "no
consumer parses Reason prose" is as valuable as the fix itself, because it bounds the blast
radius and un-poisons downstream reasoning.

## Rules

- Read-only until verdicts are in; fix in a second pass so early fixes don't bias later checks.
- Never "fix" a claim by making it vaguer. Vague claims are unverifiable, and unverifiable is
  a failure class, not a safe harbor.
- History (CHANGELOG entries, commit messages) is testimony about the past — audit it only
  for claims phrased as *current* state.
- If a claim cannot be verified cheaply, that is a finding: say so instead of guessing.
