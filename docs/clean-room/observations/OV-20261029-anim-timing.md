# OV-20261029-anim-timing: how long a texture animation phase lasts
Date: 2026-10-29
Status: confirmed (re-analysis of the OV-20261029-motion-timing round-2 traces) and implemented
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
OV-20261029-motion-timing left the phase length open. `DELAY 8` gave phases of 32 cycles and
then 28 cycles at the same frame rate. That is neither a sum of `TIME_CORR` nor whole `TICKS`.
`ONESHOT` phases were much shorter than their `DELAY`. What rule gives these lengths?

## Why It Matters
Every animated thing, actor, wall and region steps by this rule, and `EACH_CYCLE` actions run on
its loop ends. OpenVirtue summed `TIME_CORR` per phase, which drifts from the original by a few
frames per phase.

## Clean-Room Boundary
- Oracle used: the existing DOSBox-X traces of OV-20261029-motion-timing round 2 (SaintsX-patched
  DOS runtime V3.963); no new runs.
- Reference material consulted: the 1998 A3 manual p.39 (`DELAY` in ticks; "the higher the DELAY
  value, the more does the animation speed remain constant on different frame rates").
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-motion-timing/round2/` (anim-delay
  `621ee562`, anim-delay8 `42efe866`, anim-oneshot `efd2e5b5`, anim-random `ac0b253c`, `OVMOT0.TXT`).
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
A thing's `EACH_TICK` prints `TICKS`, `TIME_CORR`, a cycle counter and `MY.CYCLE` every cycle.
`TIME_CORR` alternates between 0.25 and 0.281 (9/32) at about 60 cycles a second. For every phase,
the analysis took its length in cycles and the `TIME_CORR` of the cycle before it was first
seen. That earlier cycle is the one whose animation step started the phase, because the thing's
`EACH_TICK` runs before its animation step.

## Observations
| Case | Phase lengths (cycles) per loop | `TIME_CORR` of the cycle that started the loop |
|---|---|---|
| anim-delay8 (`DELAY 8, 8`) | 32, 32 · 28, 28 · 28, 28 | 0.25 · 0.281 · 0.281 |
| anim-delay (`DELAY 1, 2, 3`) | 3, 7, 10 · 4, 8, 12 · 3, 7, 10 · 4, 8, 12 · 3, 7, 10 · 3, 7 | 0.281 · 0.25 · 0.281 · 0.25 · 0.281 · 0.281 |
| anim-random (`DELAY 2, 2`, `RANDOM 1`) | 10, 10 · 15, 15 · 13, 13 · 8, 8 · 12, 12 · 11, 11 · … (7 to 15) | (both values) |
| anim-oneshot (`DELAY 2, 2, 2`) | 3, 2, 2 from the cycle `PLAY` was set | the loop was started at level start, with `TIME_CORR` 1 |

Within a loop every phase uses the same step: 4/8/12 is 1, 2, 3 ÷ 0.25, and 3/7/10 is
⌊1, 2, 3 ÷ 0.28125⌋. The step changes only at the start of a loop, and it is the `TIME_CORR` of the
cycle in which the last phase ended. Rounding to nearest instead of down would give 4 and 11 where
3 and 10 were seen. With `RANDOM` both phases of a loop have the same length, and the length
changes from loop to loop. The `ONESHOT` phases of 2 frames are `DELAY` 2 at the step of 1 that
its loop was fixed with in the first cycle. The phase was seen once more in the cycle `PLAY` was
set.

## Invariant
- **Fixing a loop.** When an animation loop starts (in the first cycle, and in the cycle whose
  step ends the last phase), the engine takes that cycle's `TIME_CORR` as the step. It draws one
  random factor u for the loop. Each phase of the loop then lasts
  `max(1, ⌊DELAY · (1 + RANDOM·u) / step⌋)` frames, counted from the cycle after it begins.
  `EACH_CYCLE` runs when the last phase ends.
- **`ONESHOT`.** A `ONESHOT` texture waits on phase 1 until `PLAY` is set. It counts phase 1 from
  the next cycle and plays once with the step its loop was fixed with. At the end it clears
  `PLAY`, runs `EACH_CYCLE`, fixes the next loop's step and rests on the last phase.

## Open Questions
- The distribution of the `RANDOM` factor: the seen lengths (7–15 for `DELAY 2`, `RANDOM 1`) fit a
  uniform factor up to 1 + `RANDOM`, but 30 loops do not pin down its shape.
- The phase lengths at frame rates where `TIME_CORR` exceeds `DELAY` (OpenVirtue keeps one frame).

## Implementation
`WdlRuntime.AdvanceAnimationTarget` counts frames. `StartAnimationLoop` and `AnimationFrames` fix a
loop's step and random factor. `ovtool oracle diff` reproduces all 19 anim-delay phases. Tests:
`WdlRuntimeTests.Tick_AnimationLoop_FixesPhaseFramesFromTheWrapCyclesTimeCorr` and
`Tick_OneShotTexture_RequiresPlayAndStopsOnLastPhase`.
