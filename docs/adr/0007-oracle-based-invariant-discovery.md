# ADR-0007: Oracle-Based Invariant Discovery
Date: 2026-07-01
Status: accepted

## Context
ADR-0005 establishes that OpenVirtue uses original game data, SaintsX, and
third-party Acknex-3 projects as reference-only material under a clean-room
process. That answers "can we copy code?" with "no", but it does not yet define
how we should discover runtime behavior that is not visible from file formats or
public documentation.

The engine still needs hidden invariants: WDL numeric semantics, scheduler order,
`TIME_CORR`, movement/collision thresholds, palette lighting, actor animation,
AI hooks, save/load behavior, and other details that only the original runtime
fully reveals. These facts are necessary for compatibility, but the path to them
must leave a record that separates functional observations from copied
expression.

Legal background is not a substitute for attorney review, but it shapes the
engineering policy: U.S. copyright law does not extend protection to ideas,
procedures, processes, systems, methods of operation, concepts, principles, or
discoveries. See 17 U.S.C. 102(b):
<https://www.law.cornell.edu/uscode/text/17/102>. Our policy is built around
capturing those functional facts without importing protected expression.

## Decision
Adopt an **oracle-based invariant discovery** process for hidden engine behavior.

1. Prefer black-box observation of the original runtime over source-level
   inspection. The DOSBox-X/SaintsX setup is the primary behavioral oracle.
2. Every ambiguous behavior should become an observation record under
   `docs/clean-room/observations/` before implementation relies on it.
3. Observation records describe the question, setup, inputs, measurements,
   conclusion, and follow-up tests in our own words. They must not include
   proprietary assets, original scripts, disassembly, decompiled code, or
   third-party source excerpts.
4. Implementations should cite the observation ID in tests or comments when a
   behavior is not otherwise obvious from public docs or committed fixtures.
5. If source-level study is unavoidable, use a split-role process: an observer
   writes a fact-only note, and an implementer works from that note without the
   reference source open.
6. Raw captures, save files, screenshots containing copyrighted art, logs with
   large original script fragments, and other oracle artifacts stay local under
   `_research/` or `artifacts/`; they are not committed.

## Workflow
The day-to-day procedure is defined in
[`docs/clean-room/oracle-protocol.md`](../clean-room/oracle-protocol.md).

The short version:

1. State the invariant question narrowly.
2. Build the smallest legal input/setup that can answer it.
3. Run the original runtime oracle and record only functional observations.
4. Write an observation note from the measurements.
5. Add a small OpenVirtue test that encodes the conclusion.
6. Implement from the observation and test, not from protected expression.

## Alternatives considered
- **Rely only on existing third-party source/reference code.** Rejected. It
  would weaken the clean-room record and may import GPL-incompatible or
  proprietary expression.
- **Avoid original-runtime observation entirely.** Rejected. File formats and
  public docs are not enough to reach parity for timing, scheduler, movement,
  collision, AI, and UI behavior.
- **Use an informal notebook outside the repo.** Rejected. The project needs
  durable, reviewable provenance for facts that shape code.

## Consequences
- Hidden behavior gets a traceable provenance path from question -> observation
  -> test -> implementation.
- The repo gains more documentation overhead, but this is intentional: it is the
  cost of reaching parity without contaminating the implementation.
- Future contributors and agents have a concrete process to follow when they
  need to ask the old engine a question.
