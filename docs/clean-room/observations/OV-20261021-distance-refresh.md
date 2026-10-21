# OV-20261021-distance-refresh: who reads the stale DISTANCE, what refreshes it, and region VISIBLE
Date: 2026-10-21 (rounds 1–2)
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261020-view-activity (rule 5) found that the original refreshes every actor's `DISTANCE` only
in cycles in which the player moved or an actor in front of the player moved, and that the
`SKIP_FRAMES` near range reads the refreshed value. It left open:

1. Do `IF_NEAR`/`IF_FAR` (the `DIST` test, p.69–71) and the `DIST` test that lifts `SKIP_FRAMES`
   (p.113) also read the stale value?
2. Does turning the player, a moving thing, or a script's `SET` of an actor's position refresh it?
   Is a thing's `DISTANCE` stale in the same way?
3. A region's `#VISIBLE` and `SEEN` (p.53: set "as long as a floor, ceiling or wall of the region is
   visible", and once seen). LEGALISM reads `THERE.VISIBLE` in its lift actions.

## Why It Matters
OpenVirtue used the refreshed value for the `SKIP_FRAMES` near test only and kept `DISTANCE`, the
`DIST` test and the hooks on the current positions. Region `VISIBLE` was not modelled.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.53 (region `#VISIBLE`, `SEEN`), p.69–71
  (`DIST`, `IF_NEAR`, `IF_FAR`, `DISTANCE`), p.99 (`THERE`, `SYNONYM TYPE REGION`), p.111
  (`PLAYER_ARC`), p.113 (`SKIP_FRAMES`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261021-distance-refresh/` (round 1 at the top,
  `round2/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`. Traces `OVDRX0.TXT`:
  near-hook `aaca2380…`, turn-refresh `530110c3…`, thing-move and set-actor `622ea41a…`
  (identical), region-seen `6b922198…`; round 2 thing-refresh `39ce4524…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963, native Linux DOSBox-X 2026.10.01,
  `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`, `OV_TIMEOUT` 600 s. near-hook ran at about 2.6 ticks a
  cycle, the others at 8 (turn-refresh's last cycles at 0.5). Readings are in cycles; way actors'
  `SPEED` is set to their step per cycle over `TIME_CORR` in every cycle.
- Fixture generator: `ovtool oracle prepare distance-refresh --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/DistanceRefreshProbeCases.cs`) on the trace room of the earlier probes
  (x −16…48, y −24…24, 16 high, the eye at 8, `PLAYER_ARC` 1, the player at the origin facing +X).
  Things and actors are 2 × 2 sprites. The global `EACH_TICK` sampler prints a `C` line in every
  cycle with each watched thing's or actor's X, Y and `DISTANCE`, or each watched region's
  `VISIBLE` and `SEEN` (read through `SYNONYM … { TYPE REGION; }` bound with `SET`). `IF_NEAR` and
  `IF_FAR` actions print `N`/`F` with `MY.X`, `MY.Y` and `MY.DISTANCE`.
- Cases:
  - `near-hook`: two `DIST 10` way actors walk in beside the player (x = 0, where their moves
    refresh nothing), one on each Y axis from 22 to about 5 at 0.2 a cycle; the −Y one has
    `IF_NEAR`/`IF_FAR`. Both are out of view. W1 85 cycles walking in; S1 12 standing; a beam to
    (0.5, 0) and S2 12; W2 85 walking out; S3 12; a beam back to the origin and S4 12.
  - `turn-refresh`: one way actor on the −Y axis from 22 to 12 at 0.1 a cycle (beside or behind the
    player at every angle used). 18 cycles each facing 0, then 0.3, π/2, 2.8, 0, then `PLAYER_TILT`
    0.3 and back to 0.
  - `thing-move`: the same indicator walker; two still things with `DIST 10` and the hooks, 11 away
    behind the player at (−7, 4) and in front of its plane at (7, 4). M1: a script adds 0.1 to the
    one behind's X in every cycle for 20 cycles (to 9 away); M3 the same for the one in front.
    `set-actor`: the same with two still actors (no `TARGET`).
  - `region-seen`: the pad region (x −12…−4, y 12…20, the room's heights, invisible passable border
    walls) and the room, with a plain free-standing wall from (−10, −6) to (−6, −6). 8 cycles
    each: R1 the origin facing +X; R2 facing 2.034 (the pad's centre); R3 facing +X; R4 facing 1.2;
    R5 facing 1.35 (the pad's corner (−4, 20) 0.08 inside the cone, 18.6 deep); R6 at (−8, −8)
    facing +Y (the pad in the cone behind the wall); R7 at (−3, −8) facing +Y (past the wall's end).
  - Round 2 `thing-refresh`: the indicator walker, a `DIST 10` thing with the hooks behind the
    player at (−7, 4), and a way actor in front of the player's plane, out of the cone, from
    (10, 22) to (10, 18), standing until FW. M1 moves the thing in to 9 away by script; then a turn
    to 0.3 (TA); M3 moves it back out to 11; FW starts the walker in front for 12 cycles; then
    `PLAYER_SIZE` and `PLAYER_Z` 5 (PZ); then `PLAYER_ARC` 0.8 (PA).

## Observations
| Case | Trace | Reading |
|------|-------|---------|
| `near-hook` W1, S1 | both actors walk 22 → 5.4, moving every 6th cycle by 1.18 all the way, `DISTANCE` 22 throughout; no `N` line | neither `IF_NEAR` nor the `DIST` lift of `SKIP_FRAMES` sees the actors within 10 |
| `near-hook` B1, S2 | beam at cycle 97; at 99 both `DISTANCE`s read 5.917 (0.5 + 5.417) and `N` prints with `MY.DISTANCE` 5.917 | the beam refreshes; `IF_NEAR` fires in the cycle the refreshed value shows, on that value |
| `near-hook` W2, S3 | the actors walk out 5.4 → 20.4, now every cycle (near at the stale 5.917), `DISTANCE` 5.917 throughout; no `F` line | nor does `IF_FAR` see them leave |
| `near-hook` B2, S4 | beam back at 206; at 208 `DISTANCE` 20.367 and `F` prints | as at B1 |
| `turn-refresh` | `DISTANCE` changes once, to the walker's current \|dy\|, in the second `C` line after each turn (at 20, 38, 56, 74 for turns printed at 18, 36, 54, 72), after the tilt to 0.3 (92) and after the tilt back to 0 (110), and at no other time | turning and tilting refresh |
| `thing-move`, `set-actor` | both things (actors) move 2 by script, 11 → 9 away, behind and then in front; every `DISTANCE`, theirs included, stays at its first value (22, 11, 11); no `N` line. The two traces are identical | a script's `SET` of a thing's or actor's position refreshes nothing, in front or behind; a thing's `DISTANCE` is stale like an actor's, and its `IF_NEAR` reads it |
| `thing-refresh` TA | the turn after M2 (cycle 40): at 42 the thing's `DISTANCE` reads 9.008 and its `N` prints; the walker's refreshes too | a turn refreshes things with the actors |
| `thing-refresh` FW | the walker in front started at 82: at 84 every `DISTANCE` refreshes (thing 11, `F` prints) although the walker first moves at 90; at 90 its move and another refresh show together; it stopped at 94 before its next move | a walk in front refreshes (rule 5); the refresh at 84 has no visible move (open) |
| `thing-refresh` PZ, PA | the eye lowered to 5 at 106: refresh at 108; `PLAYER_ARC` 0.8 at 118: refresh at 120 | the eye height and `PLAYER_ARC` refresh too |
| `region-seen` | R1: pad `VISIBLE` 0, `SEEN` 0. R2: 1, 1 from the second line after the turn. R3: 0, `SEEN` stays 1. R4 and R5: 0. R6: 0. R7: 1. The room: 1, 1 throughout | below |

The refreshes show in the second `C` line after the change, as `VISIBLE` does after a turn or beam
(OV-20261020-view-activity): the action that turns runs after the cycle's sampler, the next sampler
still reads the previous frame.

In R5 the sliver of the pad inside the cone lies at most 18.6 deep. With the eye 8 above the floor
and 8 below the ceiling and a vertical half-tangent of about 0.36 (OV-20261009-projection-fov),
floor and ceiling come onto the screen only beyond about 22, so nothing of the pad was drawn. In
R2 its far edge lies 20–23 deep.

## Conclusion
Confirmed for patched V3.963:

1. **The `DIST` test reads the stale `DISTANCE`.** `IF_NEAR`/`IF_FAR` of things and actors and the
   `DIST` lift of `SKIP_FRAMES` compare `DISTANCE` as last refreshed, not the current distance.
   The hooks fire in the cycle in which a refresh brings the value across `DIST`, and `MY.DISTANCE`
   reads the refreshed value.
2. **Things' and actors' `DISTANCE` (|dx| + |dy|) are refreshed all at once, only when the player's
   view changed or an actor in front of the player walked.** View changes measured: a walk and a
   beam (OV-20261020-view-activity), a turn (`PLAYER_ANGLE`), a tilt (`PLAYER_TILT`), the eye height
   (`PLAYER_Z`, with `PLAYER_SIZE`) and `PLAYER_ARC`. A script's `SET` of a thing's or actor's
   position refreshes nothing, in front of the player or behind it, and does not refresh the moved
   object's own `DISTANCE` either. Reading `DISTANCE` refreshes nothing.
3. **A region is `VISIBLE` while some of it is on the screen in the last drawn frame**: in the cone,
   not hidden behind a wall, and within the screen's top and bottom, as for walls and sprites. The
   region the player stands in was visible throughout. `SEEN` is set once visible and stays.
   Changes show one frame late, as for walls.

## Confidence
high for 1 (two hooks, two beams and the `DIST` lift, one run, unambiguous). high for 2's triggers
and non-triggers (each one shown at least once, with nothing else changing; the identical
thing-move and set-actor traces agree). medium for things being refreshed by every trigger that
refreshes actors (seen for a turn and a walk in front). high for 3's cone, occlusion and `SEEN`;
medium for the vertical window (one stand, R5, explained by the measured projection).

## Open Questions
- In thing-refresh a refresh came two cycles after the walker in front was started (`SPEED` from 0
  to its step), six cycles before its first visible move. Whether a front actor that starts, or
  takes a skipped turn without moving, counts as a move is not settled; OpenVirtue refreshes on
  position changes only.
- Whether the `CLIP_DIST` border of things and actors, and walls' `DISTANCE`, are stale too. Walls
  do not move except with moving vertices, so only doors could tell.
- Whether a region whose border walls are all outside the cone but whose floor is seen from inside
  another non-convex region is visible; OpenVirtue samples the floor and ceiling along the border.

## Tests To Add Or Update
- `WdlDistanceRefreshTests`: `Tick_IfNearAndIfFar_ReadTheStaleDistanceUntilTheViewChanges`,
  `Tick_AViewChangeRefreshesEveryDistance` (angle, tilt, arc), `Tick_AnEyeHeightChangeRefreshesEveryDistance`,
  `Tick_AScriptMoveRefreshesNothing`, `Tick_SkipFrames_TheDistLiftReadsTheStaleDistance`,
  `Tick_Regions_GetVisibleAndKeepSeen`.
- The `WdlViewActivityTests` rule-5 tests keep passing.

## Implementation Notes
`WdlRuntime.RefreshEntityDistances` keeps every thing's and actor's distance and writes it to
`DISTANCE` (with the 65535.999 start of OV-20261019-actor-contact). It refreshes in `RefreshView`
at the start of a cycle when the view (eye position and height, `PLAYER_ANGLE`, `PLAYER_TILT`,
`PLAYER_ARC`) differs from the last refresh, and after the actors move when one moved to a point in
front of the player. The `DIST` test (`TryCurrentProximity`, `IsWithinDist`) and the `SKIP_FRAMES`
near range read it through `TryDistTestDistance`; walls keep their current distance
(`RefreshWallDistances`). `ViewTest.IsRegionVisible` samples a region's floor and ceiling (as thin
bands) and its drawn border walls along the part of its border inside the cone, through the same
sight-line walk as walls; for a convex floor the border holds the farthest points. Regions are
tested on demand when an action reads `VISIBLE` or `SEEN` (`TestRegionView`), the player's own
region counting as visible, and every region in every frame once `SEEN` has been read, as walls.
