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

## Replay notes (2026-11-03, harness)
- `startup` (`OVSDO0.TXT`, sha256 `8859dcb4`) now matches in full: past the last sample the replay
  continues the samples' 8/9 frame cadence instead of the median step, which placed the last line
  one step early. round5 waitt-small (`cb83adb4`) matches in full: its gap from count 3 (14.188) to
  5 (14.719) is now filled 9 then 8 thirty-seconds, ending on the 0.250 that `R13` prints (the old
  fill gave 8 then 9), and its first step is read from the lines' `TIME_CORR` column. round2
  start-wait still prints `W16` at 15.750 against 15.781: its eight samples leave the phase open
  there. `tools/README.md` describes the rules.

## Addendum: the seconds clock, as far as the traces go (2026-11-03)
No new run. `round4/anim-clock` (`OVSDO0.TXT` `69f6d4b9`) and `order-wmp` (`b1152cf8`) were re-read
for the second boundaries:

| Carrier | Last cycle before | First cycle after |
|---|---|---|
| anim-clock, `SECS` 0 → 1 | `TOTAL_FRAMES` 57, `TICKS` 15.000 | 58, 15.250 |
| anim-clock, `SECS` 1 → 2 | 118, 31.000 | 119, 31.250 |
| anim-clock, `SECS` 2 → 3 | 180, 47.250 | 181, 47.500 |
| order-wmp, `EACH_SEC` | the `SEC` lines come in cycles 6 and 68 (62 apart), at `TICKS` 15.000 and 31.250 | |

- A second takes 61 or 62 cycles, at 0.25 and 0.28125 ticks a cycle: 16.0 to 16.5 `TICKS`.
- The first boundary comes 0.75 to 1 tick before `TICKS` reaches 16.
- Later boundaries drift about 0.25 tick a second further from the multiples of 16.

No constant offset, frame count or `TICKS` threshold fits all six boundaries. That is the
behaviour of a finer real-time clock which `TICKS`, summed from 1/32-tick steps, loses ground to.
The traces print no such clock (`TOTAL_TIME` equals `TICKS`), so the replay cannot place these
boundaries. OpenVirtue keeps one clock for both, and `order-wmp`, `anim-clock` and `start-wait`
differ only there.

A probe that times `EACH_SEC` against a DOS clock it can read (for example `SET` at a flic boundary,
or a long `WAITT` cadence) would settle it.

## Round 6: a wait armed by a key hook, and a second press (2026-11-04)
`round6/hook-wait` (`OVSDO0.TXT`, fast launch): the global slot counts cycles in `so_n`; the probe
arms `WAIT 3` from its own action (`A` at 2, `R` at 5), sets `IF_ANYKEY` to an action that prints
`K`, waits `WAIT 3`, prints `KR`, waits `WAITT 16` and prints `KT`, and DOSBox-X presses Space
twice half a second apart (`AUTOTYPE -w 10 -p 0.5 space space`).

| Line | `TICKS` | `so_n` | Entry |
|---|---|---|---|
| `K` | 81.844 | 261 | 1 |
| `KR` | 82.625 | 264 | 1 |
| `K` | 90.500 | 294 | 2 |
| `KR` | 91.281 | 297 | 2 |
| `KT` | 97.844 | 322 | (entry 1, 15.2 ticks after its `KR`) |
| `KT` | 106.469 | 355 | (entry 2, 15.2 ticks after its `KR`) |

- **A wait armed from a key hook counts like any other.** `KR` came three cycles after `K`, as
  `R` after `A`: the hook's cycle counts, and the hook prints that cycle's `TICKS` and the count a
  resumed action prints in it (the global slot counts after both). This closes
  OV-20261015-flic-scheduler-detail's open question.
- **A second press while the hook's action waits starts a second instance.** The second `K`
  came 33 cycles after the first, while entry 1 was in its `WAITT 16`; both entries then printed
  their `KR` and `KT` on their own clocks (`so_c` is one global counter, so the first `KT` reads 2).
  Nothing was restarted or dropped: a key hook is an action start, as `EACH_SEC` is
  (OV-20261015-each-sec-cadence).
- Implemented: `RuntimeHost` queues key and mouse hooks (`WdlRuntime.QueueActionHook`) and the
  next cycle runs them after the resumed waits, so their lines read the cycle's `TICKS` and the
  wait they arm counts it. The replay presses a carrier's `AUTOTYPE` keys in the cycles of the
  original's hook lines (`TraceProbeCase.HookToken`, `hook_token` in `case.json`). Test
  `WdlRuntimeWaitCountTests.Tick_QueuedKeyHook_RunsInTheCycleWithItsCountAndItsWaitResumesThreeCyclesLater`;
  hook-wait replays 10 of 11 lines (the first `K` reads `TIME_CORR` 0.281 where the replay's
  frame is 0.250: the clock between the sparse samples).

## Round 7: where in the cycle a key hook runs (2026-11-04)
`round7/hook-order` (`OVSDO0.TXT`, fast launch): the main action prints `M` every cycle from a
`WAIT 1` loop while `IF_ANYKEY` prints `K`, waits `WAIT 1` and prints `KR`; one Space.

| `TICKS` | `so_n` | Lines in order |
|---|---|---|
| 80.813 | 258 | `M`, `K` |
| 81.094 | 259 | `M`, `KR` |

- **A key hook runs after the cycle's resumed waits**, and the wait it arms resumes after the
  older waiting action in the next cycle: waits resume in the order they were armed, the hook's
  last. OpenVirtue's `DispatchQueuedHooks` sits right after `ResumeWaitingActions`, and its
  waiting list keeps arming order; hook-order replays in full (1205 lines) once the replay
  presses the key within 1/1024 of the original line's three-decimal `TICKS`.

## Round 8: `IF_SPACE` and `IF_ANYKEY` together, and the key skills (2026-11-04)
`round8/hook-both` (`OVSDO0.TXT`, fast launch): `IF_ANYKEY` prints `A` and `IF_SPACE` prints `S`,
each with `KEY_SPACE` and `KEY_ANY`; the main loop prints `M` with the same skills in every
cycle where `KEY_ANY` reads 1; one Space.

| `TICKS` | `so_n` | Lines | `KEY_SPACE`, `KEY_ANY` |
|---|---|---|---|
| 81.844 | 261 | `M`, `S`, `A` | 1, 1 |
| 82.094 | 262 | `M` | 1, 1 |
| 82.344 | 263 | `M` | 1, 1 |

- **Both hooks fire, the key's own first.** `S` then `A`, after the cycle's resumed wait (`M`), as
  round 7 found. OpenVirtue queued `IF_ANYKEY` first; it now queues the key's hook and then
  `IF_ANYKEY`.
- **The key skills read 1 from the hooks' cycle on, as long as the key is down**: three
  0.25-tick cycles here, DOSBox-X's `AUTOTYPE` press. The replay holds a scripted key for that
  long (`KEY_SPACE` and `KEY_ANY` 1 in the press's cycle and the two after). hook-both then
  replays line for line but for its cycle count, one high from the press on (a sparse trace: the
  clock between its samples).
- Test `RuntimeHostInputTests.Frame_SpacePressed_RunsTheKeysOwnHookThenIfAnykeyInTheNextCycle`.
