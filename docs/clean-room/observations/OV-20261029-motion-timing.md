# OV-20261029-motion-timing: ACCEL, FRICTION and texture animation timing
Date: 2026-10-29
Status: ACCEL confirmed (three rounds); animation phase timing answered by OV-20261029-anim-timing
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
1. How does `ACCEL skill, a` (p.76) change the skill with `TIME_CORR`, `FRICTION` and `INERTIA`?
   The retail movement scripts set `FRICTION` (ground, air, wading) and `INERTIA` and drive walking,
   jumping and gravity through `ACCEL`.
2. How long does a texture animation phase last for a given `DELAY`, with `RANDOM`, and with
   `ONESHOT`, and where does `EACH_CYCLE` run relative to the object's `EACH_TICK`?

## Why It Matters
OpenVirtue guessed `v' = (1 − TIME_CORR·FRICTION)·v + TIME_CORR·a/INERTIA` with `FRICTION` 0.5 by
default (the manual's value), and stepped animation phases by exact `TIME_CORR` sums.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.39 (`DELAY`, `RANDOM`), p.52/p.59/p.70
  (`EACH_CYCLE`, `ONESHOT`, `PLAY`), p.76 (`ACCEL`), p.112 (`FRICTION`, `INERTIA`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-motion-timing/` (round 1 at the top,
  `round2/`, `round3/`).
- Relevant local-only file hashes (sha256, first 8): patched `VRUN.EXE` `bba9f9bc`; `OVMOT0.TXT`:
  accel-default `cec03ccd`, accel-friction1 `322f8bf8`, accel-inertia4 `2386cb98`, round2
  accel-negative `e8f5c50c`, round3 accel-friction-half `18724630`, accel-inertia-friction
  `9f7dee22`; round2 anim-delay `621ee562`, anim-delay8 `42efe866`, anim-oneshot `efd2e5b5`,
  anim-random `ac0b253c`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
Native Linux DOSBox-X 2026.10.01, `forcerate=60`; carriers from `ovtool oracle prepare
motion-timing` (`tools/OpenVirtue.Tools/MotionTimingProbeCases.cs`). Round 1 ran at 8 ticks per
cycle, rounds 2 and 3 at about 0.26. Each `ACCEL` line carries `TIME_CORR`, the speed before and
after one call; the `GO` line prints `FRICTION` and `INERTIA` as the case left them.

Compiler facts from round 1 (Genesis errors in `OVCON.TXT`): `ACCEL mt_v, 0 - 1` is "Error(2):
Missing/wrong parameter" (the second parameter is a number or a skill, not an expression), and
`PRINT_VALUE MY.PLAY` is "Error(66): Wrong typ MY.PLAY FIXED FLAGS" (a flag is not a FIXED skill;
it can be compared in an `IF`).

## Observations
| Case | Readings | Reading |
|---|---|---|
| accel-default (`GO … FRICTION 0.000 INERTIA 1.000`) | at `TIME_CORR` 8: 0 → 8 → 16 → 24 | `FRICTION` defaults to 0, not 0.5 |
| accel-friction0 | the same | |
| accel-friction1 (`FRICTION` 1) | 0 → 1 → 1 → 1 at `TIME_CORR` 8 | the speed settles at a/F at once, not at 8 |
| accel-inertia4 (`INERTIA` 4) | 0 → 2 → 4 → 6 | a·`TIME_CORR`/I |
| accel-decay (a = 0, v = 10, `FRICTION` 0) | 10 → 10 | |
| round2 accel-negative (a = −1 from 2) | 2 → 1.719 → 1.469 → … → 0.156, steps of `TIME_CORR` | |
| round3 friction-half (F 0.5) | 0 → 0.250 → 0.469 → 0.684 … settles at 1.993 | |
| round3 friction-quarter (F 0.25) | 0 → 0.281 → 0.514 … 3.830 | |
| round3 decay-half (F 0.5, a = 0, v = 10) | 10 → 8.594 (`TIME_CORR` 0.281) → 7.520 (0.250) … | |
| round3 inertia-friction (F 0.5, I 4) | 0 → 0.070 → 0.130 → 0.188 … still rising at 1.572 | friction divided by the inertia too: the speed heads for a/F = 2, not a/(I·F) = 0.5 |
| round2 anim-delay (`CYCLES 3`, `DELAY 1, 2, 3`) | `CYCLE` reads 1, 2, 3; phases alternate between 3, 8, 12 and 4, 7, 10 cycles (5.2 and 6.3 ticks a loop) | `CYCLE` counts phases from 1 |
| round2 anim-delay8 (`DELAY 8, 8`) | 32, 32, 28, 28, 28, 28 cycles a phase at a steady `TIME_CORR` of 0.26 (8.4 then 7.3 ticks) | not a `TIME_CORR` sum |
| round2 anim-random (`DELAY 2, 2`, `RANDOM 1`) | phases of 2.2 to 3.7 ticks | `RANDOM` adds up to its fraction of the delay |
| round2 anim-oneshot (`CYCLES 3`, `DELAY 2, 2, 2`, `ONESHOT`) | `CYCLE` 1, `PLAY` 0 until the script sets `PLAY`; then phases of 3, 2 and 2 cycles (0.5 to 0.8 ticks); `E` once with `CYCLE` 3, `PLAY` 0 | plays once, clears `PLAY`, runs `EACH_CYCLE`, rests on the last phase |
| `EACH_TICK` and `EACH_CYCLE` of one thing | in the wrap cycle `A` (the old phase) then `E` (the new phase) | the object's `EACH_TICK` runs before its animation step |
| looping textures | `MY.PLAY == 1` true throughout | |

## Invariant
- `FRICTION` defaults to 0 and `INERTIA` to 1.
- `ACCEL v, a` integrates `dv = (a − FRICTION·v) / INERTIA` per tick over the cycle's `TIME_CORR`, one
  tick at a time: `v ← v + dt·(a − F·v)/I` for steps `dt` of at most 1 tick. With F 0 that adds
  `TIME_CORR·a/I`; the speed tends to `a/F`. The result is a FIXED skill (OV-20261029-numeric-edge).
  Every one of the 247 calls traced fits within 0.0015 (the printed rounding plus one 1/1024 step).
- `CYCLE` counts phases from 1. An object steps its animation after its `EACH_TICK`; `EACH_CYCLE`
  runs when the last phase ends (OV-20261029-scheduler-order). A `ONESHOT` texture waits on its first
  phase until `PLAY` is set, plays once, clears `PLAY`, runs `EACH_CYCLE` and rests on its last phase.

## Open Questions
- Answered by OV-20261029-anim-timing: each loop fixes its phases at ⌊DELAY / TIME_CORR⌋ frames with
  the `TIME_CORR` of the cycle that starts it. The original question: the phase length. It is neither a `TIME_CORR` sum (`DELAY 8` gave 32-cycle and then 28-cycle phases
  at the same rate) nor whole ticks of `TICKS`; the counts look tied to a frame-rate estimate that
  the runtime revises (32 cycles = 8 ticks at 4 cycles a tick, 28 = 3.5). `ONESHOT` phases were much
  shorter than their `DELAY`. A probe that varies the rate during a run is needed; OpenVirtue keeps
  stepping phases by `TIME_CORR` sums.
- Whether `RANDOM`'s factor is uniform.

## Implementation
`WdlInterpreter.ExecuteAccel` and the `FRICTION` default in `WdlRuntime`; `WdlRuntime.RunObjectPass`
(the animation step after `EACH_TICK`). Tests: `WdlInterpreterTests.Accel_…`,
`WdlRuntimeTests.Runtime_AccelInstruction_CanAccelerateObjectMember`,
`Runtime_PredefinedDefaults_…`; `ovtool oracle diff` matches the round-1 `ACCEL` carriers and
round3 friction-quarter (the others differ from the anchor cycle on, whose `TIME_CORR` the
harness cannot recover).

## Replay notes (2026-11-02)
- **`ONESHOT`: `PLAY` is clear when `EACH_CYCLE` runs.** In round2 anim-oneshot (trace `OVMOT0.TXT`,
  sha256 `efd2e5b5`) the `E` line, printed by the thing's `EACH_CYCLE` in the cycle the last phase
  ends, reads `MY.PLAY` 0; the `A` line of `EACH_TICK` just before it in the same cycle still reads 1.
  OpenVirtue ran `EACH_CYCLE` first and cleared `PLAY` after it, so its `E` line read 1. Rule: at the
  end of a `ONESHOT` run the engine clears `PLAY`, then runs `EACH_CYCLE`. With it the carrier matches
  all 105 lines (was 104), and no other carrier changes. `WdlRuntime.AdvanceAnimationTarget`; test
  `WdlRuntimeTests.Tick_OneShotTexture_ClearsPlayBeforeEachCycleRuns`.
- **A looping animated texture has `PLAY` set.** In round2 anim-delay, anim-delay8 and anim-random
  (traces `OVMOT0.TXT`, sha256 `621ee562`, `42efe866`, `ac0b253c`) every `A` and `E` line prints
  `mt_p` 1 (`IF (MY.PLAY == 1)`), from the first line on; no script sets `PLAY` there. OpenVirtue
  left it 0. Rule: at level start an object whose texture loops (`CYCLES` above 1, no `ONESHOT`)
  gets `PLAY` 1; a `ONESHOT` keeps 0 until a script sets it. Whether a single-phase texture or one
  assigned later sets `PLAY` is not measured. With it anim-delay matches 56 of 158 lines (was 2),
  anim-delay8 178 of 217 (was 3) and anim-random 136 of 636 (was 2); the rest is the phase and
  `RANDOM` difference. No other carrier changes. `WdlRuntime.InitializeAnimationCycles`; test
  `WdlRuntimeTests.Runtime_LoopingAnimatedTexture_StartsWithPlaySet`.

## Replay notes (2026-11-03, harness)
- **anim-random replays the same way every time.** Its `RANDOM 1` delay factor is drawn from the
  runtime's generator, which was seeded from the clock, so the same build matched 12, 23 or 263 of
  636 lines of round2 anim-random (`OVMOT0.TXT`, sha256 `ac0b253c`) from run to run; the 136 above
  was one such draw. The replay now passes a fixed seed (`RuntimeHost.OpenArchive`'s `randomSeed`,
  1), and the carrier matches 72 lines on every run. The original's generator is not modelled, so
  the line count says nothing about `RANDOM`'s distribution. Outside the replay the runtime still
  draws unseeded. Tests: `WdlRuntimeRandomTests`,
  `RuntimeHostFrameClockTests.OpenArchive_RandomSeed_DrawsTheSameRandomOnEveryRun`.

## Replay notes (2026-11-03, night)
- **A loop's `EACH_CYCLE` runs with `CYCLE` back at 1.** In round2 anim-delay and anim-delay8
  (`OVMOT0.TXT`, sha256 `621ee562`, `42efe866`) every `E` line, printed by the thing's `EACH_CYCLE`
  in the scheduler cycle the last phase ends, reads `MY.CYCLE` 1, while the `A` line of its
  `EACH_TICK` earlier in the same scheduler cycle still reads the last phase (3, or 2). OpenVirtue ran
  `EACH_CYCLE` and then wrapped `CYCLE`, so its `E` lines read the last phase. Rule: at the end of a
  looping run the engine sets `CYCLE` 1 (and queues the first phase's texture sound), then runs
  `EACH_CYCLE`, then fixes the new loop's step and `RANDOM` factor; a `ONESHOT` still runs its hook
  with `CYCLE` at the last phase and `PLAY` clear. With it anim-delay and anim-delay8 match in full
  (158 and 217 lines); anim-random still differs by its `RANDOM` draws. 13 of 14 carriers.
  `WdlRuntime.AdvanceAnimationTarget`.

## Addendum: `RANDOM` is seeded per launch (2026-11-04)
A second launch of round2 anim-random (`OVMOT0.TXT`, 637 lines; the first launch's trace is kept
under `work/_stale/20261104T171449Z/`) gave a different sequence of phase lengths from its first
phase on (607 phase changes in both, 364 of them coinciding by chance of the 2-to-3-tick
quantisation). The original's generator is seeded from something that differs between launches
(the clock), so its draws cannot be replayed; `RANDOM`'s distribution can still be measured from
the phases, its sequence not. The carrier stays a line-count residual.

