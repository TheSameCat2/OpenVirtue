# Oracle Protocol

This is the engineering procedure for discovering hidden Acknex-3/Saints of
Virtue runtime invariants while preserving OpenVirtue's clean-room boundary.

It is not legal advice. It is the project policy we follow so that our public
code and docs are based on functional observations, public specifications, and
our own analysis rather than copied expression.

## Core Rule

Implement from committed facts, not from protected source.

The original runtime may be used as a behavioral oracle. Reference projects may
be used as a checklist for questions. Neither may be copied or ported into
OpenVirtue.

## Allowed Inputs

Preferred sources:

- Public specifications and public-domain algorithms.
- Our own byte analysis of user-supplied game data.
- Black-box runs of the original DOS/Windows runtime with controlled inputs.
- Small synthetic WDL/WMP snippets written by us.
- Measurements from the current OpenVirtue implementation.

Allowed with extra care:

- SaintsX patch diffs and third-party API/grammar references as fact checklists.
  Restate facts in our own words. Do not copy implementation expression.
- Source-level study of incompatible/proprietary references, only when black-box
  observation cannot reasonably answer the question. Prefer split roles.

Not allowed in committed work:

- Decompiler/disassembler output.
- Verbatim source from `_research/`, original scripts, or third-party projects.
- Proprietary files or large excerpts from proprietary files.
- Raw oracle captures containing copyrighted assets.

## The Six-Step Flow

### 1. Ask A Narrow Question

Good:

- "What value does `TIME_CORR` expose at a 60 fps frame?"
- "Does WDL division by zero yield 0, preserve the current value, or fault?"
- "In what order do two actors' `each_cycle` hooks run?"

Too broad:

- "How does movement work?"
- "Recreate the scheduler."

### 2. Choose The Least Contaminating Oracle

Prefer, in order:

1. Public docs or committed project docs.
2. Synthetic data observed in the original runtime.
3. Real game data observed through the original runtime.
4. Third-party/reference material as a question checklist.
5. Source-level study by an observer who will not directly implement from it.

### 3. Record The Setup Without Assets

Record:

- Runtime/version used.
- Archive or world name, if relevant.
- Checksums for local-only files, if useful.
- Emulator/config settings that affect behavior.
- The smallest input setup in our own words.

Do not commit:

- The original files.
- Long original script snippets.
- Screenshots dominated by original art.
- Binary captures or saves.

### 4. Capture Facts

Write measurements and observations as data:

- frame delta -> `TIME_CORR`
- starting state -> ending state
- object A/B order
- expression -> result
- collision input -> position/region result

Keep interpretation separate from raw observation.

### 5. Make A Conclusion And A Test

Every useful observation should end with:

- "Conclusion"
- "Confidence"
- "Open questions"
- "OpenVirtue tests to add/update"

The test does not need real game data when a synthetic fixture can express the
same invariant.

### 6. Implement From The Note

When code depends on a discovered invariant, cite the observation ID in the test
name, test comment, or nearby implementation note if the behavior is surprising.

Example:

```csharp
// OV-20260701-rule-divide-by-zero: Acknex-safe division yields 0.
```

Use these comments sparingly; prefer tests as the durable record.

## Split-Role Mode

Use split-role mode for high-risk or source-level questions.

Observer:

- May inspect reference material.
- Writes a fact-only observation note.
- Must not include code, structure, naming, or expression beyond functional
  facts required for compatibility.

Implementer:

- Reads the observation note and committed docs/tests.
- Does not inspect the reference source for that behavior while implementing.
- Writes original code and tests.

One person may do both only for low-risk black-box observation. If in doubt, use
split-role mode.

## Local Artifact Policy

Local-only artifacts may be useful during measurement:

- emulator configs
- run logs
- raw frame captures
- generated temporary WDL/WMP files
- save states
- spreadsheets of measurements

Store them under `_research/oracle-runs/` or `artifacts/oracle/`. Do not commit
them unless they have been reduced to a sanitized observation note.

## First Invariants To Measure

Prioritize small invariants that unlock the next implementation work:

1. `TIME_CORR` values under known frame deltas.
2. WDL expression edge cases: divide/modulo by zero, truthiness, logical
   short-circuiting, comparison precision.
3. `SKILL` bounds and assignment behavior.
4. `IF_START` and `each_cycle` dispatch order.
5. Player angle units, movement units, jump constants, and collision thresholds.
6. Region/wall portal crossing: step height, headroom, closed portals, and
   stuck behavior.
7. Sprite/actor animation timing and facing behavior.
8. Palette lighting/shading lookup behavior.

## Ready-To-Implement Checklist

Before implementing from an oracle result:

- [ ] Observation note is committed or staged.
- [ ] It contains no proprietary code/assets or raw reference expression.
- [ ] The question is narrow and the conclusion is testable.
- [ ] A synthetic test is possible, or a guarded real-data test is justified.
- [ ] Any source-level reference use followed split-role mode.
- [ ] The implementation can be written from the note alone.
