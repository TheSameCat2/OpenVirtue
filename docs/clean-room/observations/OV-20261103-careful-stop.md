# OV-20261103-careful-stop: a refused long step's stop, and a wall's DISTANCE after a beam
Date: 2026-11-03
Status: prepared, not yet run
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
(not yet run)
