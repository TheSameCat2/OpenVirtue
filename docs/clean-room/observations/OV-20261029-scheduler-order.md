# OV-20261029-scheduler-order: the start-up cycle and the order of a cycle's actions
Date: 2026-10-29
Status: confirmed (one round, four carriers)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
1. What do `TICKS` and `TIME_CORR` read in `IF_START` and in the first cycles, and when does a
   `WAITT` armed in `IF_START` end? (The oracle-diff harness found `WAITT 16` from `IF_START`
   ending at `TICKS` 13.4 in 60 Hz runs, before 16 ticks had passed.)
2. In one cycle, in which order do the global `EACH_TICK` slots, a region's, walls', things' and
   actors' `EACH_TICK`, `EACH_CYCLE`, `EACH_SEC` and an action resuming from `WAIT` run?
3. Does a `SET EACH_TICK` made by an action take effect in the same cycle?

## Why It Matters
OV-20260701-scheduler-if-start-cycle only showed that an `EACH_TICK` armed in `IF_START` runs at
some point. OpenVirtue started `TICKS` counting in the first cycle, stepped every texture animation
(and ran `EACH_CYCLE`) before the global slots, and ran walls, things and actors in map order.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.53/p.59/p.66 (`EACH_TICK`, `EACH_CYCLE` of
  walls, regions, things and actors), p.80 (`WAITT`), p.96 (global `EACH_TICK` slots), p.117
  (`TICKS`, `TIME_CORR`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-scheduler-order/`.
- Relevant local-only file hashes (sha256, first 8): patched `VRUN.EXE` `bba9f9bc`; traces
  `OVSDO0.TXT`: startup `8859dcb4`, order `e2f305d3`, order-wmp `b1152cf8`, set-midcycle `9f480095`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Native Linux DOSBox-X 2026.10.01, `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`;
  `VRUN.EXE -NJ TITLE -RUN`. Carriers from `ovtool oracle prepare scheduler-order`
  (`tools/OpenVirtue.Tools/SchedulerOrderProbeCases.cs`). The runs went at about 0.26 ticks per
  cycle (`TIME_CORR` alternating 0.250 and 0.281).
- Every line is a token, `TICKS` and `so_n`, the runs of the global slot-1 action, which counts
  first; equal `TICKS` mean the same cycle.
- `startup`: the top of `IF_START` selects the trace, prints `S0`, arms `EACH_TICK.1` (prints `Y`
  with `TIME_CORR` every cycle) and prints `S1`; then the scaffold's `WAITT 16`, `W16`, `WAIT 1`,
  `W1`, `WAIT 1`, `W1B`, `WAITT 1`, `WT1`, `WAITT 4`, `WT4`.
- `order`: global slots 2 and 1 (armed in that order), `EACH_SEC.1`, the room's `EACH_TICK`, two
  walls, two things and two actors with `EACH_TICK` (declared a before b, placed b before a), a
  thing and a wall with a 2-phase animated texture and `EACH_CYCLE`; the main action prints `M`
  after every `WAIT 1` for 70 cycles. `order-wmp` places a before b.
- `set-midcycle`: slot 1 arms slot 2 and a thing's `EACH_TICK` when `so_n` is 5 and clears slot 2
  at 9.

## Observations
| Case | Trace | Reading |
|---|---|---|
| startup | `S0 0.000 0 1.000`, `S1 0.000 0 1.000`; `Y 0.000 1 1.000`, `Y 0.313 2 0.313`, `Y 0.594 3 0.281`, … `Y 13.156 51`; `W16 13.438 51`, `Y 13.438 52`; `W1 13.688 52`; `W1B 13.969 53`; `WT1 14.219 54`; … `Y 17.094 66`; `WT4 17.375 66` | `IF_START` reads `TICKS` 0 and `TIME_CORR` 1. The slot armed there first runs in cycle 1, which also reads `TIME_CORR` 1 and `TICKS` 0; `TICKS` counts from cycle 2. `WAITT 16` from `IF_START` resumed in cycle 52 (`TICKS` 13.438), before the slot. `WAIT 1` resumed in the next cycle twice. `WAITT 1` armed in cycle 54 resumed in 55; `WAITT 4` armed in 55 resumed in 67 (12 cycles, 3.156 ticks) |
| order | per cycle `M, (SEC), G1, G2, R, AA, AB, (TC), TA, TB, (WC), WA, WB`; `SEC` at 15.000 and 31.250 | the resumed `WAIT` first, then `EACH_SEC`, then the global slots in slot order (slot 2 was armed first), then the region, the actors, the things and the walls. Things placed tb, ta, tc ran tc, ta, tb; actors placed ab, aa ran aa, ab; walls placed wb, wa, wc ran wc, wa, wb |
| order-wmp | per cycle `M, G1, G2, R, AB, AA, (TC), TB, TA, (WC), WB, WA` | placed in declaration order, b runs before a: each kind runs from the last placed to the first |
| set-midcycle | `G1 14.469 5`, `ARM 14.469 5`, `G2 14.469 5`, `TA 14.469 5`; …; `G1 15.531 9`, `DIS 15.531 9`, `TA 15.531 9` (no `G2`) | a slot or object `EACH_TICK` set by an earlier action runs in the same cycle, and a slot cleared earlier in the cycle does not |

`EACH_CYCLE` (`TC`, `WC`) ran in the object's own place in its kind's pass. The motion-timing probe
(OV-20261029-motion-timing, `anim-delay8`) shows a thing with both hooks: in the cycle of the wrap
its `EACH_TICK` ran first (still showing the old phase), then the phase changed and `EACH_CYCLE`
ran.

## Invariant
- `IF_START` and the first cycle read `TIME_CORR` 1 (and `TIME_FAC` 1); `TICKS` and the second
  stay at 0 in the first cycle and grow by `TIME_CORR` from the second cycle on.
- A wait counts `TIME_CORR` (`WAITT`) or one (`WAIT`) down at the end of every cycle, the one it was
  armed in included, and its action resumes at the start of the first cycle with less than 1 left
  (OV-20261015-flic-scheduler-detail). `IF_START` counts as such a cycle with `TIME_CORR` 1 for
  `WAITT`: 16 − 1 (`IF_START`) − 1 (cycle 1) − `TICKS` < 1 first holds at `TICKS` 13.156, so the
  action resumes in the next cycle, at 13.438. The same rule gives the 8-ticks-per-cycle runs of the
  earlier probes their `TICKS` 24.
- A cycle runs: resumed waits; the `EACH_SEC` slots (when the second completes); the global
  `EACH_TICK` slots in slot order; the player's region events and the proximity events (placed
  by OV-20261019-actor-contact round 8); then one object pass — regions, actors, things, walls, each kind
  from the last the map places to the first — in which each object runs its `EACH_TICK` (read when
  its turn comes) and then steps its texture animation, running `EACH_CYCLE` when a cycle ends.

## Round 2 (2026-10-30)
- `regions`: the room (first in the WMP) and the pad island (third) each print in their
  `EACH_TICK`. Every cycle ran `M, G1, P, R`: regions also run from the last the map places to the
  first. Hash `609b2cfb`.
- `start-wait`: `IF_START` arms the slot (`Y` with its cycle count) and then `WAIT 3`. `W3` printed
  at the start of cycle 3 (`W3 0.594 2`, before that cycle's `Y`). So a frame-count `WAIT` armed in
  `IF_START` counts `IF_START` as one of its cycles, as `WAITT` does. Hash `c004cbf2`.

Implementation: `WdlRuntime.RunStartup` counts down every wait `IF_START` armed, `WAIT` and
`WAITT` alike. `RuntimeHostRegionEventTests.Frame_LocateAfterBeam_FiresLeaveAndEnterOnce` now
expects the `LOCATE` a cycle earlier. Both round-2 traces replay up to the last line, which is
printed after the sampler has stopped, so the replay clock cannot place it.

## Round 3: fractional waits (2026-10-30)
`frac-wait`: the probe action runs `WAIT 1`, `1.5`, `0.5`, `2.5`, `0` and `1`, printing after each
one (hash `a0fed566`). The cycle counts were 1, 1, 0, 2, 0 and 1. A wait goes on while one or more
cycles are left, so `WAIT 1.5` takes one cycle and `WAIT 0.5` none at all: the action does not
wait. `WdlInterpreter.ExecuteWait` now skips a `WAIT` below 1; `WAITT` keeps any positive amount
(unmeasured below 1). The carrier's cycle counts replay; its `TICKS` do not, since it has no
per-cycle sampler.

## Round 4: the `EACH_CYCLE` clock, from `order-wmp` (2026-10-30, analysis only)
`order-wmp` (hash `b1152cf8`) runs a thing and a wall that share a two-frame texture (`CYCLES 2`,
`DELAY 1, 1`), each with an `EACH_CYCLE`.
- **Order within a cycle.** The cycle runs AB, AA, TC, TB, TA, WC, WB, WA: an object's
  `EACH_CYCLE` runs in its own place in the object pass. Things and walls each run in reverse map
  order. OpenVirtue's order is the same; only the timing differs.
- **Which cycles.** The two objects always fire in the same cycle. After their first firing
  (cycle 5, `TICKS` 14.719) they fire every 8, 8, 6, 6, 8, 8, 8, 6, 6 cycles. The pattern repeats
  every 36 cycles: 10 animation frames per 36 cycles. Over those 36 cycles `TIME_CORR` (equal to
  each cycle's `TICKS` step, 0.25 or 0.28125 here) adds up to 9.4375, not 10.
- **What does not fit.** No accumulator of `TIME_CORR` matches the cycles: with or without carry,
  with any start phase, any per-cycle extra or any integer step and threshold. Neither does a whole
  number of cycles per frame computed from `TIME_CORR`. A 6-cycle gap holds only 1.5625 ticks, less
  than the two `DELAY` ticks. So the texture clock is not `TICKS`. It is likely a finer real-time
  clock that `TICKS`, summed from truncated steps, falls behind (about 6% here). That clock cannot
  be read from this trace.

OpenVirtue advances textures by `TIME_CORR`, so in the replay it fires every 8 cycles after its
first firing (cycle 2).

`round4/anim-clock` (hash `69f6d4b9`) has one such thing and prints `TIME_CORR`, `TOTAL_TIME`,
`TOTAL_FRAMES` and `SECS` every cycle. These skills are not in the A3 manual, but the original's
saves list them.
- **`TOTAL_TIME`** equals `TICKS` on every line, so it is not another clock.
- **`TOTAL_FRAMES`** counts cycles. **`SECS`** counts 16-tick seconds; its first step comes at
  cycle 58.
- **Firings.** All 19 `EACH_CYCLE` firings fall on even `TOTAL_FRAMES`, 6 or 8 cycles apart
  (never 7), and the gaps change within one second. So it is not a per-second frame rate either.
- **Every-other-frame models.** A check every other cycle, accumulating the `TICKS` passed at any
  rate from 0.9 to 1.25 with carry, does not fit either.

What drives the texture clock is still open; the even frames suggest the object's animation is
stepped every second cycle.

`round4/defaults` (hash `635c53ee`) prints the movement and input skills in the start action before the
carrier template sets `PLAYER_SIZE` 8, after it, and after `PLAYER_SIZE` 4. They are fixed start
values, not derived from the player's size:

| Skill | V3.963 start | Manual / OpenVirtue before |
|---|---|---|
| `PLAYER_SIZE` | 3.5 | 3 |
| `PLAYER_CLIMB` | 2 | 1.5 |
| `WAVE_PERIOD` | 20 | 16 |
| `WALK_PERIOD` | 4 | 4 |
| `KEY_SENSE` | 1 | 0.7 |
| `MOUSE_CALM` | 1 | 3 |
| `MUSIC_VOL` | 0.5 | 1 |
| `PLAYER_WIDTH` | 1.199 | 1.2 |

OpenVirtue now starts with these. The oracle summary lost no match.

## Round 5: `WAITT` below one tick (2026-10-30)
`round5/waitt-small` (`OVSDO0.TXT` `cb83adb4`): a global slot counts cycles, and the probe arms
`WAITT` 0, 0.1, 0.25, 0.3, 0.5, 0.75, 0.99, 1 and 1.3 one after another.
- Every wait below 1 went straight on: its `R` line prints in the same cycle as its `A` line.
- `WAITT 1` resumed in the next cycle, and `WAITT 1.3` two cycles later.
- So `WAITT`, like `WAIT` (round 3), does not wait at all for less than one. OpenVirtue had waited
  a cycle for any positive `WAITT`; it now goes straight on below 1.

## Open Questions
- Whether a flic started in `IF_START` makes its first cycle a start-up cycle (the implementation
  keeps the flic's delay-based `TIME_CORR`).
- Texture animation phase lengths (see OV-20261029-motion-timing, and round 4: the texture clock
  is not `TICKS`).

## Implementation
`WdlRuntime.UpdateTimingSkills` (first cycle), `WdlRuntime.RunStartup` (the `IF_START` countdown),
`WdlRuntime.RunObjectPass`. Tests: `WdlRuntimeTests.Tick_FirstCycle_KeepsTimeCorrAtOneAndTicksAtZero`,
the start-up cycle in the `WdlRuntimeEachSecTests`/`WdlRuntimeWaitCountTests` helpers,
`RuntimeHostFrameClockTests`; `ovtool oracle diff` on the probe matches `startup` (but the last
line's tick, past the last sample) and `set-midcycle`.
