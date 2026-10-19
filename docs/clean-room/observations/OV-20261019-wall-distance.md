# OV-20261019-wall-distance: What a wall's MY.DISTANCE reads and what its DIST test compares
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-proximity-events measured the `DIST` test of things and actors, and their
`MY.DISTANCE`, as |dx| + |dy| between the player and the object's centre. It left walls open.
The manual (p.51–52) measures a wall's `DIST` border and its `#DISTANCE` to the wall's nearest
vertex, calls the value approximate (±20 %), and says it is only valid within the player's
`CLIP_DIST`. Which metric does the original use for walls: |dx| + |dy| to the nearest vertex, the
straight-line distance to the nearest vertex, or the distance to the wall segment? When is the
value refreshed, and what happens outside `CLIP_DIST`?

## Why It Matters
OpenVirtue compared a wall's `DIST` with the distance to the wall segment and gave walls no
`DISTANCE` skill. Near the middle of a long wall the two metrics differ by up to half the wall's
length. The manual warns about exactly this case: with a `DIST` below half the length,
`IF_NEAR` does not fire near the wall's centre (p.51).

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.51–53 (wall `DIST`, `#DISTANCE`, `IF_NEAR`,
  `IF_FAR`, `EACH_TICK`, `PASSABLE`), p.30 and the region `CLIP_DIST` entry (ch. 8).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-wall-distance/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261019-proximity-events). Traces `OVWDS0.TXT`: wall-front `6c10f59c…`, wall-behind
  `4872c736…`, wall-clip `845c3278…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. The runs
  averaged 3.8 (wall-front), 3.0 (wall-behind) and 1.6 (wall-clip) cycles per tick. Part of
  wall-behind and most of wall-clip ran at 8 ticks per cycle.
- Fixture generator: `ovtool oracle prepare wall-distance --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/WallDistanceProbeCases.cs`). It uses the room and the global
  `EACH_TICK.1` sampler of the proximity-events probe. A free-standing, visible, `PASSABLE` wall
  runs from v1 (24, −8) to v2 (24, 8), with the room on both sides, `DIST 6`, `IF_NEAR`, `IF_FAR`
  and an `EACH_TICK` action. The `EACH_TICK` action prints `MY.DISTANCE` every 2 ticks with the
  player position and the position from its previous run. `IF_NEAR`/`IF_FAR` print every run with
  `MY.DISTANCE` and the player position. The player walks at 0.2 steps per tick on four paths:
  - A: along y = 0 from x = 0 to 36, through the wall's centre.
  - B: beamed to (0, −12), then along y = −12 to x = 36, 4 steps beyond v1.
  - C: beamed to (36, 12), then along y = 12 to x = 0, 4 steps beyond v2.
  - D: beamed to (10, −22), then along (+1, +1) through v1 to (34, 2).

  As in the proximity-events probe, the walks drift slightly in +y (up to 2.7 steps over a
  36-step walk). The printed positions are used throughout.
- Cases:
  - `wall-front`: the player looks along +X (`PLAYER_ANGLE` 0).
  - `wall-behind`: `PLAYER_ANGLE` 3.1416, so the wall is behind the player while x < 24.
  - `wall-clip`: as wall-front, in a room with `CLIP_DIST 10`.

## Observations
All runs 2026-10-19 15:44–15:48 UTC, three launches, one per case, all rc 0 with the completion
marker and no compiler error in `OVCON.TXT`.

| Case | Trace | Reading |
|------|-------|---------|
| `wall-front`, `EACH_TICK` | 373 samples from 0.271 to 31.666. All 373 equal \|dx\| + \|dy\| from the printed position to the nearer vertex, to three decimals. 64 also match the previous position: 60 while standing, and 4 where the old and new positions give the same value. None match the straight-line or segment distance | `MY.DISTANCE` is \|dx\| + \|dy\| to the nearest vertex, at the position of the current cycle |
| `wall-front`, paths B–D | B: `N` at (20.470, −10.465), 5.995; `F` at (28.125, −9.891), 6.016. C: `N` at (25.166, 12.813), 5.979; `F` at (22.925, 12.980), 6.056. D: `N` at (21.030, −10.970), 5.939; `F` at (27.036, −4.964), 6.072. Each value is \|dx\| + \|dy\| to v1 (B, D) or v2 (C) | `DIST` is compared with the same value. Both vertices count. The straight-line distance would have fired B at x ≈ 19.5 and D at (19.8, −12.2); the segment distance would have left D at (30, −2) |
| `wall-front`, path A | No `N` while approaching the centre (the segment comes within 6 at x = 18). One `N` in the cycle the player passed through the wall, at (24.009, 1.801) with `MY.DISTANCE` 6.208, above `DIST`. No `F` afterwards. Path B's `N` then fired normally (count 2) | the centre of a wall longer than 2 × `DIST` does not fire. Passing through the wall fires `IF_NEAR` regardless of `DIST` |
| `wall-behind` | 316 samples, each within 0.001–0.028 of \|dx\| + \|dy\| to the nearer vertex. The deviation has the same sign on each side of a vertex, flips when the player crosses x = 24, and is about 0.021 steps while standing at a whole-number position. No rotation, offset or scale model fits it better than 0.027. The `N`/`F` positions are within 0.02 steps of wall-front's. Path A's pass-through `N` fired at (25.5, 0.063), 9.423 | the value and the `DIST` test do not depend on whether the wall is in view. With the player facing −X the value is very slightly off (0.2 % at most), cause unknown |
| `wall-behind`, beam to (36, 12) | `CD` (the beam, printed in the run action) with `pe_n` 918, then a `W` line in the next sampler cycle (919) at the same `TICKS`: `MY.DISTANCE` 12.917 (the value before the beam), previous position (35.569, −9.332). The next sample reads 16.017 | the value is computed once per cycle, before the actions run. A beam earlier in the same cycle is not reflected until the next cycle |
| `wall-clip` (`CLIP_DIST 10`) | `W` lines only while `MY.DISTANCE` < 10 (largest 9.982). The first sample after each gap is exact, and its "previous position" is from many cycles earlier. `N`/`F` fired on all paths (B: 5.633/7.027, C: 5.980/6.037, D: 5.922/6.061, 8 ticks per cycle in B), and path A's pass-through `N` at 9.438 | a wall's `EACH_TICK` only runs while its `DISTANCE` is within the player's `CLIP_DIST`, measured to the nearer vertex. Outside it, the distance and the `DIST` test still work |

## Conclusion
Confirmed for patched V3.963:

1. **A wall's `MY.DISTANCE` is |dx| + |dy| between the player and the nearer of the wall's two
   vertices**, the same metric as for things and actors. It is computed once per cycle, before
   the cycle's actions, so a beam made in an action shows in the next cycle.
2. **A wall's `DIST` is compared with that value.** `IF_NEAR`/`IF_FAR` follow the rules of
   OV-20261019-proximity-events (once per crossing), and their actions read the compared value. Near
   the centre of a wall longer than 2 × `DIST` the test never fires, as the manual warns (p.51).
3. **Neither depends on the view or on `CLIP_DIST`.** The value is computed for a wall behind
   the player and for a wall outside `CLIP_DIST`, and the `DIST` test runs for both.
4. **Passing through a wall fires its `IF_NEAR`** in the cycle of the crossing, even when the
   player is outside `DIST`, with no `IF_FAR` afterwards. The manual describes this for `DIST 0`
   (p.51); it also happens with `DIST 6`.
5. **A wall's `EACH_TICK` runs only while its `DISTANCE` is within the player's region's
   `CLIP_DIST`.**

## Confidence
high for rules 1–3: 373 exact samples and six crossings separate the three candidate metrics,
and the other two cases agree. The once-per-cycle timing rests on one sample, read right after a
beam. medium for rules 4 and 5: path A shows rule 4 three times, and rule 5 comes from one case,
with the boundary seen only from the inside (9.982).

## Open Questions
- The wall-behind deviation (up to 0.028 steps with `PLAYER_ANGLE` 3.1416). wall-front and
  wall-clip (angle 0) are exact. Things were only measured at angle 0 too.
- Pass-through `IF_NEAR`: does it fire again while the player is already within `DIST`? Path D
  crossed the wall at v1 itself while within `DIST`, and nothing fired, so that crossing may not
  count. Does touching a solid wall fire it, does a beam across the wall count, and what happens
  with `DIST 0` (p.51)?
- `EACH_TICK` and `CLIP_DIST`: is the border `<` or `≤`, and do things and actors follow
  `THING_DIST`/`ACTOR_DIST` (p.30)?
- Whether things' and actors' `MY.DISTANCE` is also computed only once per cycle. OpenVirtue
  refreshes theirs again after the actors move, which picks up a beam within the cycle.
- Region: OpenVirtue only tests a wall's `DIST` while the player is in one of the wall's
  regions. Not probed.

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_ComparesWallDistWithSumOfAxisDistancesToNearestVertex`
  (replaces `Tick_PlayerProximity_TriggersWallNearFromSegmentDistance`): 1 step from the middle
  of a wall longer than 2 × `DIST` does not fire, nor does 2.5 straight-line (3.5 by axes) from a
  vertex. 1.5 + 1 from v1 and 1 + 1 from v2 fire, and `IF_NEAR`/`IF_FAR` read the compared value
  as `MY.DISTANCE`.
- `WdlRuntimeTests.Tick_EachTickActions_ReadWallDistanceToNearestVertex`: a wall's `EACH_TICK`
  reads |dx| + |dy| to the nearer vertex, and a wall without hooks has the value too.
- `RuntimeHostWallDistanceTests.Frame_BeamInAnAction_ShowsInWallDistanceFromTheNextCycle`: in
  the cycle of a beam the wall's `EACH_TICK` reads the distance from before the beam, and in the
  next cycle the new one.

## Implementation Notes
`WdlRuntime.TryProximityDistance` uses |dx| + |dy| to the nearer vertex for walls.
`RefreshDistances` sets every wall's `DISTANCE` at the start of the cycle only, before the
resumed actions. A beam reaches the player's position only when the host applies
`PLAYER_X/PLAYER_Y` after the cycle, so it shows in the next cycle (rule 1). Things and actors keep both refresh
points. The `DIST` test stores the value it compared for walls too. As for things, OpenVirtue
computes the value for every wall in any region, and a script write lasts until the next
refresh.

Rules 4 and 5 were measured further and implemented from OV-20261019-wall-pass-clip: passing
through or pushing against a wall fires `IF_NEAR` while the player is outside `DIST`, and the
`EACH_TICK` gate is `DISTANCE < CLIP_DIST`, for things and actors scaled by `THING_DIST`/`ACTOR_DIST`.
