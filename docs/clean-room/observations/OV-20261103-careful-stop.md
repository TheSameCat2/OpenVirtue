# OV-20261103-careful-stop: a refused long step's stop, and a wall's DISTANCE after a beam
Date: 2026-11-03
Status: confirmed (seven carriers at 60 frames a second) and implemented; one carrier (a walker placed in rock) left as read
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
1. **The refused long step.** A skipped `CAREFULLY` way actor's six-cycle step, and the half of it,
   both end inside a pad 2 steps up. OV-20261020-view-activity long-pad's walker from 23 (3
   before the north border) stopped at 21.049, the 1074-unit hold of a normal step;
   OV-20261019-border-kinds actor-climb-2's walker from 22 (2 before it) stopped at 21.524, 487
   units short of its start, half the clear distance (2048 − 1074)/2 (view-activity 2026-11-03
   addendum). Which rule holds, and what decides between them: the gap, the frame rate (long-pad
   ran at 60 frames a second, actor-climb-2 at 8 ticks a cycle), or the side?
2. **A wall's `DISTANCE`.** view-activity berkeley-near read a wall's `DISTANCE` 9.990 at (16, 0),
   ten units under |dx| + |dy| = 10 to either vertex of the wall from (10, −4) to (10, 4), where
   the stands at (8, 3) and the origin read 3 and 14 exactly. What is short, and when?

## Why It Matters
Retail guards patrol out of sight on `SKIP_FRAMES` and stop at doors and ledges; a wall's
`DISTANCE` gates `IF_NEAR`, `IF_FAR` and `BERKELEY` walls in every level.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `forcerate=60`, the SaintsX-patched DOS runtime
  V3.963 (`VRUN.EXE` sha256 `bba9f9bc`); trace files only.
- Reference material consulted: A3 manual p.53 (walls, `DISTANCE`), p.73 (`CAREFULLY`), p.113
  (`SKIP_FRAMES`, `ACTOR_CLIMB`); no other.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261103-careful-stop/`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
`ovtool oracle prepare careful-stop` (`tools/OpenVirtue.Tools/CarefulStopProbeCases.cs`) on the
trace room of the earlier probes with the pad island (x −12…−4, y 12…20) 2 steps up behind plain
walls. The player stands at (40, 0) facing +X, so the walkers are unseen and skip.
- **`north-*`, `south-*`.** Two 2-wide `CAREFULLY` way actors at x = −10.5 and −5.5, `SPEED` set to
  1.6 over `TIME_CORR` in every cycle (a six-cycle step of 9.6), each starting its own gap before a
  border and walking across the pad: 22/22.5, 23/23.5 and 24/24.5 south toward 2 (`north-2-25`,
  `north-3-35`, `north-4-45`); 10/9 and 8/7.5 north toward 23 (`south-2-3`, `south-4-45`). The
  sampler prints both positions in every cycle for 48 cycles.
- **`wall-dist`.** A plain free-standing wall from (10, −4) to (10, 4) read through a synonym:
  stands of six cycles at (16, 0) facing −X and +X, (16, 1), (15, 0), (12, 3), (8, 3), the origin,
  (20, 4), (10, 8), and (16, 0) again, the `W` line printing the wall's `DISTANCE` with the
  player's position and angle in every cycle.

## Predictions
- Walkers: the clearance stop (1.049 from the border in every case), half the clear distance
  (start ∓ (gap − 1.049)/2 in every case), or a split by the gap or the side.
- Wall: exact |dx| + |dy| everywhere, ten units short at (16, 0) only, or short whenever |dy|
  equals the vertex's y.

## Observations
All seven carriers ran (rc 0) at 60 frames a second, `TIME_CORR` 0.25 and 0.28125. Launches are in
`launches.md`; the single step was 1638 units and the six-cycle step 9828 (9.598 in |dx| + |dy|).

### The walkers (OVCST0.TXT)
Gap is the distance from the walker's start to the border it faces; the hold is 1074 units (1.049).

| Case | Start | Moves (printed a cycle later) | Reading |
|---|---|---|---|
| `north-2-25` a1 | 22 (gap 2) | 21.524 at 7, then 11.929 (through), 2.332, 2.000 (arrived), 10.951, 20.545 | 487 of the 974 units to the hold: half |
| `north-2-25` a2 | 22.5 (2.5) | 21.049 at 7, 11.454, 2.000, 10.951, 20.549, 22.500 | the hold |
| `north-3-35` a1 | 23 (3) | 21.049 at 7, 11.454, 2.000, 10.951, 20.549, 23.000 | the hold |
| `north-3-35` a2 | 23.5 | 22.951 at 1, 21.049 at 7, 11.451, 2.000, … | placed 0.5 from the room's edge (y 24): put at its hold first |
| `north-4-45` a1 | 24 | 22.951 at 1, 21.049 at 7, … | placed on the room's edge: the same |
| `north-4-45` a2 | 24.5 | 22.101 at 7, 21.575 at 13, 11.979 at 19, 2.385, 2.000, 10.951, 20.549 | placed in rock outside the room: walks in; the quarter step, then half the clear distance (538 of 1077), then through |
| `south-2-3` a1 | 10 (2) | 10.476 at 7, 20.069 (through), 23.000, 21.049, 11.451, 10.000 | half (487 of 974) |
| `south-2-3` a2 | 9 (3) | 10.951 at 7, 20.545, 23.000, 21.049, 11.451, 9.000 | the hold |
| `south-4-45` a1 | 8 (4) | 10.399 at 7, 10.675 at 13, 20.271 at 19, 23.000, 21.049, 11.454, 8.000 | the quarter step (2.399), then half (282 of 565), then through |
| `south-4-45` a2 | 7.5 (4.5) | 9.899, 10.425 (538 of 1077), 20.021, 23.000, 21.049, 11.454, 7.500 | the same |

Other readings in the same traces:
- From 2.000 (way end) north, the six-cycle step ends at 11.596, 0.404 before the south border: the
  walker went to the hold, 10.951, not to the half step (6.8).
- A step that ends past a border it may not cross, within its half width, passes (11.979, 11.929,
  20.021, 20.069 …), as OV-20261020-view-activity's 2026-11-03 addendum said.
- north-3-35 a2, held 0.549 before its way's end (23.5) by the room's edge, turned for the next
  point: a step that would reach the point arrives though a wall stops it short.
- A walker exactly 1.0 from the room's edge (23.000, its way's end) was not put at the hold; the ones
  0.5 from it and on it were, in their first cycle (an arrival), with x nudged +4.
- The walkers 0.07 and 0.55 from the pad's step border (11.929, 11.454, 20.271, 20.021) were not put
  at any hold and walked on: the hold applies to walls with rock beyond them.

### The wall (OVCST0.TXT `W` lines)
`wall-dist`: `DISTANCE` after each beam, one cycle later: S1 (16, 0) facing 3.1416 after the origin
10.000; S3 (16, 1) 9.000; S4 (15, 0) **8.991**; S5 (12, 3) facing 0 3.000; S6 (8, 3) 3.000; S7 the
origin 14.000; S8 (20, 4) facing 3.1416 10.000; S9 (10, 8) **3.996**; S10 (16, 0) **9.990**. The
short readings are |dx| + |dy| × 1023/1024 and come at the stands whose facing did not change.

Round 2 `wall-angles`, at (16, 0) twice and (15, 0) once per facing:

| Facing | (16, 0) after a turn | (16, 0) again | (15, 0), no turn |
|---|---|---|---|
| 0 | 10.000 | kept | 9.000 |
| 0.5 | 10.000 | kept | 7.414 |
| 1.0 | 10.000 | kept | 7.027 |
| 1.5708 | 10.000 | kept | 8.991 |
| 2.0 | 10.000 | kept | 7.767 |
| 2.5 | 10.000 | kept | 6.609 |
| 3.1416 | 10.000 | kept | 8.991 |
| 4.0 | 10.000 | kept | 6.636 |
| 4.7124 | 10.000 | kept | 8.991 |
| 5.5 | 10.000 | kept | 7.054 |

Every (15, 0) value is min over the wall's two vertices of |dx·c − dy·s| + |dx·s + dy·c| with c and
s the facing's cosine and sine cut to 22.10 (0.5: 898 and 490; 1.0: 553 and 861; 1.5708 is 1608
units, sine 1023; 2.0: −426 and 931; 2.5: −820 and 612; 4.0: −669 and −774; 5.5: 725 and −722),
the vertex's |depth| + |lateral| in the player's view. After a turn the value is the map's
|dx| + |dy| whatever the facing, and a beam to the same point refreshes nothing. The walk and
border carriers of the earlier probes, facing +Y, read the map's value in every cycle
(OV-20261019-border-kinds plain-border 4.359), so a walk by velocity gives the map's value too.

## Conclusion
1. **A skipped `CAREFULLY` actor's long step** is tried whole, then at half, then at a quarter of
   its length. An end that is clear is taken. An end before a wall the actor may not cross, within
   its half width, sends the actor to the hold, 1074 units before the wall. An end past such a
   border goes on to the next halving; past it at the quarter still, the actor moves half the way
   (in whole units, toward zero) to the hold.
2. **A `CAREFULLY` actor nearer than its half width to a solid wall** (rock beyond it) is put at
   the wall's hold in place of its move, the untouched axis nudged 4/1024; exactly its half width
   away it is left, and a step border without rock beyond it holds nothing. The arrival at a way
   point it stands on still counts. A step that would reach its way point arrives though a wall
   stops it short.
3. **A wall's `DISTANCE`** is refreshed when the player's position or facing changed: after a walk
   or a turn it is the map's |dx| + |dy| to the nearer vertex (OV-20261019-wall-distance); after
   a script beam without a turn it is the nearer vertex's |depth| + |lateral| in the player's
   view, cosine and sine of the 22.10 facing cut to 22.10. That is berkeley-near's 9.990.

## Confidence
High for 1 (eleven stops over five carriers and the three earlier ones, every unit accounted for)
and for the hold of 2 (three walkers, one control). Medium for 3's two paths: ten facings and ten
stands fit, but why a beam differs from a walk and a turn is not known; a walk off the axes was
not tried here.

## Open Questions
- A walker placed in rock outside the room walks in as if nothing were there (north-4-45 a2);
  OpenVirtue keeps it out. Not modelled.
- Whether things' and actors' `DISTANCE` take the view's value after a beam as walls' do
  (OV-20261021-distance-refresh measured turns and walks facing +X).
- Whether a walk by velocity off the axes, or a turn with a walk, keeps the map's value.

## Tests To Add Or Update
- `WdlViewActivityTests.Tick_SkipFrames_RefusedLongStep_StopsAsMeasured` (gaps 2 to 4.5),
  `Tick_SkipFrames_CarefulActorStandingWithinItsHalfWidthOfASolidWall_IsPutAtTheHoldInPlaceOfItsMove`,
  `Tick_SkipFrames_PushedWalkerThenTakesItsRefusedStepToTheHold`; the long-pad test's stopper now
  expects the hold.
- `WdlRuntimeTests.Tick_WallDistance_IsTheViewsManhattanAfterAMoveAndTheMapsAfterATurn`.

## Implementation Notes
- `WdlRuntime.TestCarefulEndPoint` classifies an end as clear, too close or past a border;
  `AdvanceActorToward`'s careful branch halves twice and moves half the way to the hold.
- `WdlRuntime.TryCarefulHoldPosition`/`HoldCarefulActorAt` through
  `LevelCollisionWorld.HoldOffWalls` (strict, solid regions only), before the move; the standing
  arrival in `ArriveIfStandingOnWayPoint`; the requested step's reach in `AdvanceActorToward`.
- `WdlRuntime.RefreshWallDistances` keeps the frame of the last refresh (`TryWallDistance`), the
  view's after a scripted beam (`Player.LastMoveWasScripted`, set by `WdlPlayerMotion.Apply`).
- Result: 455 of 668 carriers match in full (446 of 661 before, 6 of this probe's 7); long-pad,
  berkeley-near, border-kinds actor-climb-2 and actor-climb-2d gained.
