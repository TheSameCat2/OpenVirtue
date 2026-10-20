# OV-20261019-border-kinds: IMPASSABLE and INVISIBLE walls, actor climbing, SKIP_FRAMES and the eye at a floor
Date: 2026-10-19 (round 1), 2026-10-20 (rounds 2–3)
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-border-climb and -eye-clamp left these wall and region-border questions open:

1. What do `IMPASSABLE` (p.53: impenetrable; with `PASSABLE`, passable for the player but not for
   actors) and `INVISIBLE` walls do to the player and to actors, free-standing and between regions?
2. Do actors climb into a raised region, and does `ACTOR_CLIMB` (p.113, "default 1") or
   `PLAYER_CLIMB` limit them?
3. How far above the new floor must the eye lie for the player to enter a region?

## Why It Matters
OpenVirtue ignored `IMPASSABLE`, treated `INVISIBLE` walls like visible ones, let `CAREFULLY`
actors into any neighbouring region, and had no `ACTOR_CLIMB` default. Retail levels use
invisible walls as trigger lines and region borders, and guards patrol stairs.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.51–53 (wall flags), p.66–73 (actor `TARGET`,
  ways, `CAREFULLY`), p.110 (`PLAYER_CLIMB`), p.113 (`SKIP_FRAMES`, `ACTOR_CLIMB`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-border-kinds/` (round 1 at the top,
  `round2/`, `round3/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`. Traces `OVBKX0.TXT`:
  imp-border, actor-climb-2 and actor-climb-2d `3af2e1d2…` (identical), imp-pass-border
  `ff91be65…`, plain-border and actor-climb-1 `b406e0c4…` (identical), eye-edge `4feeaf1b…`,
  invisible `a9065b55…`, invisible-border `e1841309…`, climb-player-3 `d16e7d24…`,
  climb-actor-05 `7b000d5b…`, climb-player-05 `5e1c2322…`, climb-skip0 `998248b0…`,
  climb-eq `1c59c9e6…`, climb-tall `62f2a404…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963, native Linux DOSBox-X 2026.10.01,
  `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. Most runs went at 8 ticks per cycle; imp-pass-border and
  invisible sped up to about 0.5 ticks per cycle midway. No reading depends on the rate.
- Fixture generator: `ovtool oracle prepare border-kinds --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/BorderKindsProbeCases.cs`), on the room, pad island, sampler, walk and
  beam helpers of the earlier trace probes. Room `SKILL1 1`, pad `SKILL1 2`, printed as
  `HERE.SKILL1`.
- Cases:
  - `imp-border`, `imp-pass-border`, `plain-border`: the pad's walls are `IMPASSABLE`,
    `IMPASSABLE, PASSABLE` or plain, the pad level with the room, with `IF_NEAR` (`DIST 0`). The
    player walks from (−8, 6) across the pad's south wall and back, then stands at (8, −8) facing
    away for 400 ticks. Two 2-wide way actors at 0.2 steps per tick cross the pad:
    `CAREFULLY` on (−10.5, 4) ↔ (−10.5, 22) and plain on (−5.5, 4) ↔ (−5.5, 22). The sampler
    prints their positions and `HEIGHT` every 16 ticks.
  - `actor-climb-1`, `-2d`, `-2`: plain pad walls with the pad 1 step up, 2 steps up, and 2 steps
    up with `ACTOR_CLIMB` 3. Round 2 sets `SKIP_FRAMES` 0 and prints the climb skills:
    `climb-player-3` (pad +2, `PLAYER_CLIMB` 3), `climb-actor-05` (pad +1, `ACTOR_CLIMB` 0.5),
    `climb-player-05` (pad +1, `PLAYER_CLIMB` 0.5), `climb-skip0` (pad +2, defaults). Round 3:
    `climb-tall` (an 8-high actor, pad +2, `ACTOR_CLIMB` 3) and `climb-eq` (pad +1,
    `ACTOR_CLIMB` 1).
  - `eye-edge`: plain pad walls, pad floor 3, `PLAYER_SIZE` 1, `PLAYER_CLIMB` 10, so only the
    eye test can block. Walks into the pad with `PLAYER_Z` 2.9, 2.99, 3, 3.01 and 3.1.
  - `invisible`: free-standing walls at x = 24, each with `IF_NEAR`: `INVISIBLE` (y −20…−8),
    `INVISIBLE, IMPASSABLE` (−4…4), `PASSABLE` (8…20), and a plain one at x = 36. The player walks
    +X into each. `invisible-border`: `INVISIBLE` pad walls with the pad 4 steps up; the player
    walks in, then from inside the pad out and back.

## Observations
| Case | Trace | Reading |
|------|-------|---------|
| `imp-border` | the player stops at y 10.751–10.755 with `N` in each of the 4 pushing cycles, no `ENTER`. The `CAREFULLY` actor stops at y 10.951–10.955. The plain actor patrols 4 ↔ 22. Later the `CAREFULLY` actor is seen at 20.51 and 22, then 11.96: it moved 9.6 steps at once, over the whole pad | an `IMPASSABLE` region border blocks the player like a solid wall, and blocks a `CAREFULLY` actor 1.05 from it. A plain actor ignores walls. The jumps come from `SKIP_FRAMES` (below) |
| `imp-pass-border` | the player enters (`ENTER`, `N` 4.359) and leaves (`LEAVE`, `N`). The `CAREFULLY` actor stays at 10.95 throughout; the plain actor patrols | as p.53: passable for the player, not for actors |
| `plain-border`, `actor-climb-1` | identical traces: both actors patrol 4 ↔ 22, the player crosses. In actor-climb-1 both actors read `HEIGHT` 0 on the pad | a plain level border is crossed by actors, and a 1-step rise is climbed at the defaults; `HEIGHT` stays relative to the floor |
| `actor-climb-2`, `-2d` | identical to imp-border: the `CAREFULLY` actor stops at 10.95 (and jumps over later), with and without `ACTOR_CLIMB` 3 | a 2-step rise stops it, and `ACTOR_CLIMB` 3 does not help this 2-high actor |
| `climb-player-3` | `SKL`: `ACTOR_CLIMB` 1.100, `PLAYER_CLIMB` 3, `SKIP_FRAMES` 0. The `CAREFULLY` actor stops at 10.95 and stays there | `PLAYER_CLIMB` does not lift actors; the default `ACTOR_CLIMB` is 1.1 |
| `climb-actor-05` | `ACTOR_CLIMB` 0.500: the `CAREFULLY` actor stops at 10.95 before the 1-step pad | `ACTOR_CLIMB` is the actor's limit |
| `climb-player-05` | `PLAYER_CLIMB` 0.500: the `CAREFULLY` actor climbs the 1-step pad | confirms: not `PLAYER_CLIMB` |
| `climb-eq` | `ACTOR_CLIMB` 1.000: the `CAREFULLY` actor stops before the 1-step pad | a rise equal to `ACTOR_CLIMB` blocks |
| `climb-tall` | the 8-high actor with `ACTOR_CLIMB` 3 climbs the 2-step pad | the 2-high actor was stopped by its own height |
| `climb-skip0` | with `SKIP_FRAMES` 0 the plain actor's samples change every 24 ticks (14.03, 9.25, 4.47, …); at the default (actor-climb-2d) they come in equal pairs and move 9.6 steps every 48 ticks. The `CAREFULLY` actor never jumps the pad | unseen actors outside `DIST` move only every 6th cycle by six cycles' distance at the default `SKIP_FRAMES` 5 (p.113); 0 moves them every cycle |
| `eye-edge` | `PLAYER_Z` 2.899, 2.989 and 3.000: blocked at 10.751–10.755 with `N`. 3.010 and 3.100: `ENTER`, then `PLAYER_Z` 3.350 in the pad | the eye must lie strictly above the new floor. The clamp then lifts it to floor + 0.35 |
| `invisible` | the player walks through the `INVISIBLE` wall and through the `INVISIBLE, IMPASSABLE` wall to x 28.75, with no `NA`/`NB`. Through the `PASSABLE` wall with one `NC`. The plain wall stops it at 34.751–34.755 with `ND` in every pushing cycle | a free-standing `INVISIBLE` wall is no obstacle and fires no `IF_NEAR` on passing, even with `IMPASSABLE` |
| `invisible-border` | the player walks onto the pad 4 steps up (`ENTER` at y 12.001, `N`), off it (`LEAVE`, `N`) and on again | an `INVISIBLE` region border is crossed whatever the heights, and the crossing fires its `IF_NEAR` like any region border (OV-20261019-clip-contact) |

## Conclusion
Confirmed for patched V3.963:

1. **An `IMPASSABLE` wall stops the player unless it is also `PASSABLE`**, also as a level
   region border, and pushing against it fires `IF_NEAR` in every cycle. **It always stops a
   `CAREFULLY` actor**, `PASSABLE` or not, 0.05 beyond the actor's half width.
2. **An `INVISIBLE` wall never stops the player**, even when `IMPASSABLE`, and as a region border
   it ignores the climb and headroom tests. Passing a free-standing one fires nothing; crossing an
   invisible region border fires its `IF_NEAR`.
3. **A `CAREFULLY` actor enters a raised region only when the floor rises less than
   `ACTOR_CLIMB` and less than the actor's own height.** A rise equal to `ACTOR_CLIMB` blocks.
   `PLAYER_CLIMB` does not apply. `ACTOR_CLIMB` starts at 1.1, not the manual's 1. An actor
   without `CAREFULLY` passes every wall and border.
4. **`SKIP_FRAMES` works as p.113 describes**: unseen actors outside their `DIST` move only every
   (`SKIP_FRAMES` + 1)th cycle, by that many cycles' distance, and such a long step can carry a
   `CAREFULLY` actor over a wall.
5. **The eye must lie strictly above the new region's floor** for the player to enter it.

## Confidence
high for rules 1, 2 and 5: every walk agreed, with flag combinations that separate them. high for
rule 3's `ACTOR_CLIMB` part (four values on two rises). medium for the height part: two actor
heights only, so "less than the height" and "less than half the height plus one" are not told
apart. medium for rule 4: the counting was read from samples 24 ticks apart.

## Open Questions
- Whether a `CAREFULLY` actor climbs down any drop, and whether it needs headroom under the new
  ceiling.
- What "not visible" means for `SKIP_FRAMES` (the actors were behind the player), and whether the
  long step is collision-tested along the way or only at its end.
- Whether `INVISIBLE` walls stop `CAREFULLY` actors.

## Tests To Add Or Update
- `PlayerTests.MoveHorizontal_ImpassableRegionBorder_BlocksEvenALevelPad`,
  `MoveHorizontal_ImpassableAndPassableRegionBorder_LetsThePlayerThrough`,
  `MoveHorizontal_InvisibleRegionBorder_IgnoresTheClimbAndCountsTheCrossing`,
  `MoveHorizontal_InvisibleFreeStandingWall_IsPassedWithoutATouchEvenWhenImpassable`.
- `WdlActorMotionTests.Tick_CarefulMove_ClimbsARiseBelowActorClimbAndItsOwnHeight` (five rows),
  `Tick_CarefulMove_ImpassableWallStopsTheActorEvenWhenPassable`; the existing
  `Tick_CarefulMove_StopsAtSolidWallWhenObstacleIsInvisible` now expects the 0.05 clearance.
- The eye test is already `PlayerTests.MoveHorizontal_IntoRegionBorder_EntersOnlyWhenTheEyeLiesBetweenTheNewFloorAndCeiling`.

## Implementation Notes
`Player.CanCross` lets the player through a `PASSABLE` or `INVISIBLE` wall, stops it at an
`IMPASSABLE` one, and otherwise applies the border-climb test. `LevelCollisionWorld.AddPassedWalls`
skips free-standing `INVISIBLE` walls. `WdlRuntime.ActorCanCross` (for `CAREFULLY` moves) stops at
`IMPASSABLE`, needs `PASSABLE` for a wall with one region on both sides, and limits a rise to below
`ACTOR_CLIMB` and below the actor's texture height. `CAREFULLY` moves now pass
`Player.WallClearance`. `ACTOR_CLIMB` defaults to 1.1. `SKIP_FRAMES` is not implemented yet: it
needs a view test for actors.
