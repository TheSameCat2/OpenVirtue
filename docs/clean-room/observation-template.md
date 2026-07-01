# Observation Template

Copy this template into `docs/clean-room/observations/` and rename it using the
observation ID, for example:

```text
OV-20260701-time-corr-60fps.md
```

Do not commit original game files, raw captures, save states, screenshots full
of original art, decompiled/disassembled code, or third-party source excerpts.

---

# OV-YYYYMMDD-short-topic: Title
Date: YYYY-MM-DD
Status: proposed | confirmed | superseded
Observer: <name or handle>
Implementer: <name or handle, if different>

## Question
What exact invariant are we trying to learn?

## Why It Matters
Which OpenVirtue behavior, test, or milestone depends on this?

## Clean-Room Boundary
- Oracle used:
- Reference material consulted:
- Split-role mode used: yes | no | not needed
- Local-only artifacts:
- Confirmation that no proprietary code/assets are committed:

## Setup
- Original runtime:
- Emulator/host settings:
- Game/archive/world:
- Input fixture or scenario:
- Relevant local-only file hashes, if useful:

## Observations
Record facts, not implementation guesses.

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
|               |                         |       |

## Conclusion
State the smallest functional rule supported by the observation.

## Confidence
low | medium | high

Explain why.

## Open Questions
-

## Tests To Add Or Update
-

## Implementation Notes
Optional. Keep this free of copied expression. Link code/tests after they exist.
