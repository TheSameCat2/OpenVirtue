# Clean-Room Workflow

This directory contains the practical clean-room process for OpenVirtue.

ADR-0005 sets the broad rule: original game files, SaintsX, and third-party
Acknex-3 projects are reference-only material; OpenVirtue ships only our own
code. ADR-0007 adds the missing day-to-day process for discovering hidden engine
behavior through an oracle without importing protected expression.

## Documents

| File | Purpose |
|------|---------|
| [`oracle-protocol.md`](oracle-protocol.md) | How to ask the original runtime behavioral questions safely. |
| [`observation-template.md`](observation-template.md) | Copy this shape when recording a new invariant. |
| [`observations/`](observations/) | Committed, sanitized observation notes. No raw assets or captures. |

## What Belongs Here

- Narrow questions about original behavior.
- Fact-only descriptions of test setup, inputs, and observed outputs.
- Small tables of measurements, counts, timings, and state transitions.
- Links to OpenVirtue tests that encode the conclusion.
- Notes about uncertainty and what still needs calibration.

## What Does Not Belong Here

- Original game assets, WRS/WDL/WMP/PCX/WAV files, screenshots full of original
  art, save files, or executable bytes.
- Decompiled or disassembled source.
- Verbatim chunks of proprietary scripts or third-party reference code.
- Notes that say "copied from <repo>" or "ported from <file>".

Raw local artifacts should live under `_research/` or `artifacts/`, both outside
the committed clean-room record.

## Observation IDs

Use this format:

```text
OV-YYYYMMDD-short-topic
```

Examples:

- `OV-20260701-time-corr-60fps`
- `OV-20260701-rule-divide-by-zero`
- `OV-20260701-portal-step-height`
Keep IDs stable once referenced by tests or implementation notes.
