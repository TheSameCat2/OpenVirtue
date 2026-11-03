# OV-20261103-startup-clock: the start-up frames and the start-up push-out
Date: 2026-11-03
Status: confirmed (one round, 17 carriers); implemented in the replay clock and the engine's push-out
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
1. Which frames does the original run between level start and a probe's first sample? The
   oracle-diff replay has to plan them, and an actor's skip phase counts them: view-activity's
   `pair-*` and `solo-radial` carriers differed only by a start-up about 3 frames too short.
2. When the player starts inside a thing or actor, in which cycle and by what arithmetic is it
   pushed out? OV-20261029-overlap fitted √(c² + 0.093) to the end distance but missed by up to
   0.008 off the axes and after the WMP coordinates were cut to 1/1024 (round 3 `d21`). Below-beam
   round 2 left the player at (0, 0) beside 16-wide objects 8.25 away, where OpenVirtue pushed it.

## Why It Matters
Every trace carrier starts with this start-up. Its frame count sets the phase of skipped actors,
`WAITT` ends and animation. The push-out sets the player's starting position in retail levels whose
placements overlap the start.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: OV-20261029-scheduler-order (`startup`), OV-20261029-overlap.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261103-startup-clock/`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle prepare startup` (`tools/OpenVirtue.Tools/StartupProbeCases.cs`), run
  headless (`SDL_VIDEODRIVER=dummy`), all rc 0.
- A global `EACH_TICK` sampler armed at the top of `IF_START`, before the settling `WAITT 16`,
  prints `TICKS`, a cycle counter, `TIME_CORR` and `PLAYER_X`/`PLAYER_Y` × 1024 every cycle.
- `clock-1` … `clock-4`: the same empty room, four launches. `push-*`: one actor near the start
  (0, 0). `last-far`, `last-near`, `thing-last`, `actor-after-thing`: an overlapping 2-wide actor or
  thing and a far one at (20, 0), in both placement orders. `wide16-3`, `wide16-bb`, `wide8-49`:
  16-wide actors 3 and 8.25 away and an 8-wide one 4.9 away.
- `OVSUP0.TXT` sha256 (first 8): clock-1 `ce18a1b9`, clock-2 `1abc6e73`, clock-3 `1abc6e73`,
  clock-4 `f7f6d0ca`, push-d21 `041d20fa`, push-d10 `41aeb573`, push-d01 `a6c3c6cb`,
  push-d005 `43819f47`, push-w4-14 `0b98cdd4`, push-negx `031a8a27`, last-far `f9a63e21`,
  last-near `9e6fc946`, thing-last `09276071`, actor-after-thing `fb470b69`, wide16-3 `f0a5744a`,
  wide16-bb `41046da6`, wide8-49 `3d729f9f`.

## Observations

### The start-up frames
In every launch, cycle 1 reads `TICKS` 0 and `TIME_CORR` 1. Each later cycle's `TIME_CORR` is the
step into it. Steps are in 1/32 tick:

| Launch | Step into cycle 2 | Then | `WAITT 16` ends in |
|---|---|---|---|
| clock-1 … clock-4, push-d01, push-negx, push-w4-14, last-far, thing-last | 10 | 8 and 9 | cycle 52 (13.406 or 13.438) |
| last-near | 9 | 8 and 9 | cycle 52 |
| push-d10 | 15 | 8 and 9 | cycle 51 |
| push-d21, wide8-49 | 16 | 8 and 9 | cycle 51 |
| push-d005, actor-after-thing | 17 | 8 and 9 | cycle 51 |
| wide16-3 | 20, then nine steps of 16–17 | 8 and 9 | cycle 41 |
| wide16-bb | 16, then eight steps of 16–17 | 8 and 9 | cycle 43 |

- clock-2 and clock-3 wrote identical traces.
- From cycle 3 on, the 8s and 9s are one cadence, floor(φ + k·R) with R ≈ 8.388 units a cycle.
  Fitted to cycles 52–169 alone and run backward, that cadence gives every launch except the two
  16-wide ones its exact cycle count to the first sample and its exact step into cycle 2. Only
  single 8/9 pairs swap here and there (at most 2 per launch).
- Run backward, the cadence also puts a virtual cycle before cycle 2. That cycle's time is past 0
  in every launch: 1, 2, 6, 7 or 9 units. The cycle before it is at 0 or earlier. So cycle 1 is the
  earliest cadence cycle past the clock's start. The step into cycle 2 is the time since that start.
- A 16-wide sprite close to the camera slows the first nine or ten frames to about 2 per tick.
  No later sample shows this.

### The start-up push-out
- **When.** Always in cycle 2, in one jump. push-d005 is the exception: it moved to (−307, 4) in
  cycle 2 and out to (−2224, 25) in cycle 3.
- **Which object.** `last-far` and `actor-after-thing` were pushed although the last placed object
  is the far one. So the first overlapping object counts, as OV-20261029-overlap said, whichever
  kind is placed last.
- **Reach.** `wide16-3` was pushed to (−6351, 4): 9.202 from the centre, so its contact is the full
  9.2. `wide16-bb`, the same actor at (8, −2), 8.246 away, left the player at (0, 0) throughout.
  `wide8-49` (4.9 away, contact 5.2) was pushed to (−314, 4).
- **Distances.** In raw units, from the centre along the line between the centres:

| Object | Centre (raw) | Contact c | End (raw) | End distance |
|---|---|---|---|---|
| 2 wide | (2150, 0) | 2252 | (−122, 4) | 2272 |
| 2 wide | (1536, 0) (round 3 `d15`) | 2252 | (−736, 4) | 2272 |
| 2 wide | (1024, 0) | 2252 | (−1250, 4) | 2274 |
| 2 wide | (102, 0) | 2252 | (−2190, 4) | 2292 |
| 4 wide | (409, −102) | 3276 | (−2789, 695) | 3296 |
| 16 wide | (3072, 0) | 9420 | (−6351, 4) | 9423 |
| 8 wide | (5017, 0) | 5324 | (−314, 4) | 5331 |

## Conclusion
- **Start-up clock.** The start-up runs the run's 60 Hz cadence from cycle 2. Cycle 1 is the
  earliest frame of that cadence whose time is past the clock's start. It reads `TICKS` 0. The step
  into cycle 2 is the rest, 9 to 17 thirty-seconds. The replay can therefore plan the start-up by
  running the first samples' cadence backward. That holds unless the start-up renders slowly.
- **Push-out arithmetic.** All in 22.10 raw units, cut toward zero unless noted:
  - Coordinates are cut toward zero (−0.1 is −102, not −103), and `PLAYER_WIDTH` 1.2 is 1228.
  - L² = (dx² + dy²) / 1024, where (dx, dy) is the player's offset from the object's centre.
  - C² = c² / 1024 + 98, where c is half the object's width plus `PLAYER_WIDTH`.
  - The object counts when L² < C² and L² < 64 · 1024 (8 steps).
  - The ratio C² · 1024 / L² is cut. Its root √(ratio · 1024) is cut.
  - The end is the centre plus the offset × root / 1024, rounded.
  - A push with no part along one axis adds 4 along that axis (OV-20261029-overlap).

  This reproduces 18 of the 21 push-outs measured here and in OV-20261029-overlap. It misses the two
  4-wide ones off the axes (round 2 `w4-diag`, and `w4-14` in the table) by one unit, and
  view-activity `range-fine`, eight overlapping actors, by one unit in y. The constant term reads
  as 0.096 units²; OV-20261029-overlap's fit gave 0.093.

## Confidence
High for the start-up clock: 15 launches, exact cycle counts and first steps, plus the
scheduler-order `startup` launch. High for the 8-step reach on this evidence, but its exact edge
is open. Medium-high for the push arithmetic: one constant fitted to 21 cases, with the 4-wide
residual open.

## Open Questions
- **The 4-wide residual.** For c = 3276 the term is 96 or 97, not 98. Round 2 `w4-diag` and
  `w4-14` land one unit closer than the rule gives.
- **The reach edge.** Is the 8-step limit Euclidean or per axis (|dx| < 8)? The one excluded case
  has dx = 8 exactly. Is it the same in walking contact?
- **push-d005.** A start offset of 51 (L² = 2) first moved the player only to −307, a quarter of
  `PLAYER_WIDTH` from the start, and pushed it out the cycle after. OpenVirtue pushes once.
- **Slow start-ups.** A start-up that renders at about 2 frames a tick cannot be planned from later
  samples.
- **view-activity `pair-*`.** With the planned start-up, the far actors' first skipped move comes
  two cycles later than the original's (cycle 10 against 8), where it came three later before. The
  skip schedule during the start-up is not settled.

## Tests To Add Or Update
- `OracleTraceTests.StartupFrames_SixtyHertzSamples_RunTheirCadenceBackToCycleOne` (clock-1 and
  push-d005 samples), `StartupFrames_SlowSamplesOrFirstCycleSampled_PlanNothing`,
  `ReplayClock_BeforeTheFirstSample_RunsThePlannedStartUp`.
- `WdlRuntimeTests.Apply_PlayerStartingInsideAnActor_IsPushedOutIn2210Steps` (d21, d15, d10, d01,
  w8-14, wide16-3, wide16-bb). The two beam push-out tests now expect the 22.10 end points.

## Implementation Notes
- `OracleTrace.FitLeadingCadence` fits the first samples' cadence. `OracleTrace.StartupFrames`
  runs it backward to cycle 1. `OracleReplay.ReplayClock` plays those frames before the first
  sample and falls back to its earlier planning when no cadence fits.
- `LevelCollisionWorld.PushOutOfEntities` does the push in raw integers, with
  `PushSlackSquaredRaw` (98) and `PushReachSquaredRaw` (8 steps). The walking contact
  (`ContactDistance`, √(c² + 0.093)) is unchanged.
- Diff against the 2026-11-03 baseline: the start-up plan adds 7,923 matched lines and 10 full
  carriers. eye-clamp `stop-border`, `stop-solid` and `vz-move` match in full, as do view-activity
  `skip-pad`, `skip-phase` and `skip-wall`, and actor-step round 4 `w-c-diag`, `w-c-y-slow` and
  `w-p-diag`. Two carriers lose lines:
  - actor-events `way`: lines 3–121 now match, where line 3 differed before; the break is now at a
    way-point arrival, 19.998 against 20.000.
  - actor-events `follow-border`: the skip phase now matches; a constant (0.031, 0.062) offset from
    one start-up burst remains.

  Both old matches came from a start-up with the wrong phase. The push-out adds 18 full carriers:
  overlap `d21`, `d15`, `d01`, `w4-14`, `w8-14`, `still-off` and `way-off`, all six below-beam
  round 2 carriers, and five of this probe's. It loses `d005`'s `PX` line, which the old model had
  matched within tolerance.

## Addendum: the timing column of a sparse event token (2026-11-03)
This one is about the harness, not the runtime. border-climb `climb-1` (`OVBCX0.TXT` `30c59e82`),
`climb-2` (`8c1baa37`), `climb-4` (`e54bf343`) and `climb-14` (`28c677eb`), and border-kinds
`invisible` (`a9065b55`), print `N`/`ND` only while the player pushes: 197 lines in cycles
120–2293. Their `TK` sampler prints every 62 cycles from cycle 1. Timing detection took `N`, which
has the most distinct `TICKS`, and the replay's clock ran wrong between pushes.

`OracleTrace.DetectTimingColumn` now drops a counted token that fills under a quarter of the cycles
another counted token spans. It takes the one counting from the earliest cycle instead.
- eye-clamp `vz-rate-c50000` keeps its `V`, printed every other cycle.
- wall-pass-clip `solid-dist0`/`solid-dist6` take `TK`, not the wall's out-of-phase `W` event.

Diff gain: 821 matched lines, none lost (`OracleTraceTests.DetectTimingColumn_SparseEventToken_*`,
`DetectTimingColumn_SamplerEveryOtherCycle_IsKept`).
