# OV-20261019-clip-contact: Region-border walls, object contact, LIBER/BERKELEY and the CLIP_DIST gate on object actions
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-wall-pass-clip left six questions open:

1. Does walking through a wall between two regions fire its `IF_NEAR`? Does pushing against a
   wall between two regions that the player cannot step across fire it, as for a solid
   free-standing wall?
2. Does a thing or actor with `LIBER` (p.71) run its `EACH_TICK` outside
   `CLIP_DIST × THING_DIST`/`ACTOR_DIST`? What does `BERKELEY` (p.71) do?
3. Are a thing's and an actor's other actions (p.69: they "will only be carried out if the
   object is within CLIP_DIST or has the flag LIBER") gated too: `IF_NEAR`/`IF_FAR` with a `DIST`
   larger than the border, and `EACH_CYCLE`?
4. Does a FOLLOW or WAY actor stop moving outside the border, and does it start again?
5. Does pushing against a solid thing or actor fire its `IF_NEAR` in every cycle, as for a solid
   wall?
6. With the player in a region whose `CLIP_DIST` differs from the object's region, whose value
   gates?

## Why It Matters
OpenVirtue gated only object `EACH_TICK` by `CLIP_DIST`. It ran `IF_NEAR`/`IF_FAR`,
`EACH_CYCLE` and actor movement at any distance, had no contact rule for things and actors, and
treated `BERKELEY` as an ordinary flag. Retail levels lower `CLIP_DIST` per region and use `LIBER`
and `BERKELEY` on guards and props, so these rules decide which actors move and which triggers fire.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.30 (`CLIP_DIST`), p.51–53 (wall `DIST`,
  `IF_NEAR`, `PASSABLE`), p.56–58 (the region keyword list and its order, region `CLIP_DIST`),
  p.64–71 (actor `TARGET` keywords, including a way as target, p.66; thing/actor `DIST`,
  `IF_NEAR`, `IF_FAR`, `EACH_CYCLE`, `EACH_TICK`, `LIBER`, `BERKELEY`), p.98–99 (synonyms),
  p.105 (`THING_DIST`, `ACTOR_DIST`), p.110 (`PLAYER_CLIMB`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-clip-contact/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`; the second batch under `rerun/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261019-wall-distance). Traces `OVCCX0.TXT`: border-pass, border-plain and border-high
  `80f7e1c9…` (identical), border-ceil `3ce2a2b4…`, berkeley `20d6adda…`, berkeley-front
  `0a32dc98…`, liber `fd014831…`, clip-near `c234835f…`, clip-move `a8c8688c…`, clip-move-liber
  `b603126a…`, contact-thing and contact-actor `cd4b2f50…` (identical), clip-regions `19ad9a2a…`,
  clip-regions-order `74b97e4c…`, clip-regions-walk `f907812c…`, clip-regions-swap `ca8d9962…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. The first
  batch (ten cases) ran at 8 ticks per cycle; the second batch (six cases) at 3.5–3.7 cycles
  per tick. No reading below depends on the rate.
- Fixture generator: `ovtool oracle prepare clip-contact --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/ClipContactProbeCases.cs`). It reuses the room, the global sampler and
  the walk and beam helpers of the proximity-events probe, and the counting `EACH_TICK` of
  wall-pass-clip (it prints its token, run count, `MY.DISTANCE` and the player position at most
  every 2 ticks). The generator's pad island (x −12…−4, y 12…20, region 2) can now take a case's
  own boundary wall and a raised floor. `IF_NEAR`/`IF_FAR` actions print every run.
- Cases:
  - `border-pass`, `border-plain`, `border-high`, `border-ceil`: the pad's four boundary walls
    carry `IF_NEAR`/`IF_FAR` (`DIST 0`), are visible, and are `PASSABLE` (pass, high, ceil) or have
    no flag (plain). The pad's floor is 0 (pass, plain), 14 (high) or 16, the ceiling height
    (ceil). The room has `SKILL1 1`, the pad `SKILL1 2`, printed as `HERE.SKILL1`; the pad prints
    `IF_ENTER`/`IF_LEAVE`. Three walks: across the south wall and back, through the whole pad
    (both walls) and back, and a diagonal one across the south wall.
  - `liber`: `CLIP_DIST 10`. A plain thing, a `LIBER` thing, a plain actor and a `LIBER` actor,
    each with a counting `EACH_TICK`. The player stands 24 or more from all of them, then 6 from
    each in turn (18 or more from the others).
  - `berkeley`: no `CLIP_DIST`. Behind the player, 10 away, a `BERKELEY` thing (`DIST 4`,
    `IF_NEAR`/`IF_FAR`), a plain thing and a `BERKELEY` actor (`DIST 4`). The player stands,
    turns to face them, turns back, then stands 3 from the thing and 3 from the actor.
    `berkeley-front`: the `BERKELEY` thing and the plain thing 10 in front of the player from the
    start; stand, turn away, turn back, stand 3 from the thing. `PLAYER_ANGLE` is printed.
  - `clip-near`: `CLIP_DIST 10`. A plain thing, a `LIBER` thing and a plain actor, each with
    `DIST 15`, `IF_NEAR`/`IF_FAR`, a counting `EACH_TICK` and a 2-phase animated texture whose
    `EACH_CYCLE` counts into a skill the sampler prints. For each, the player walks from 24 away
    to 4 away and back, standing at both ends.
  - `clip-move`, `clip-move-liber`: `CLIP_DIST 10`. A FOLLOW actor at (40, −12) and an actor
    with `TARGET` set to a two-point way (30, 12) ↔ (46, 12), both `SPEED 0.2`, without and with
    `LIBER`. Synonyms set to the two actors let the sampler print their positions. The player
    stands at the origin for 160 ticks, is beamed 9 from the FOLLOW actor and walks away at 0.5
    steps per tick, then is beamed 6 from the way actor's start, then to (43, 8).
  - `contact-thing`, `contact-actor`: three solid 4-step wide things or actors (no `TARGET`) with
    `IF_NEAR`/`IF_FAR` and `DIST` 0, 2 (below the contact distance of about 3.2) and 8. For each,
    the player walks into it, pushes for about 26 ticks, stands against it and backs off.
  - `clip-regions`: the room has `CLIP_DIST 10`, the pad `CLIP_DIST 30`. A thing in the room at
    (−8, −8), a thing in the pad at (−6, 18) and a passable wall in the room from (−14, −2) to
    (−14, 2), each with a counting `EACH_TICK`. The player is beamed with `LOCATE` to stands in the
    room and in the pad. `clip-regions-order`: the same with `CLIP_DIST` before `SKILL1`, the
    manual's keyword order. `clip-regions-walk`: the player walks into the pad and out.
    `clip-regions-swap`: the room at 30, the pad at 10.

## Observations
All runs 2026-10-19 17:39–17:56 UTC, seventeen launches. The first `liber` carrier did not
compile (a skill name collided with an object keyword, `Error(135)` in `OVCON.TXT`, rc 3); the
fixed carrier ran in the second batch. Every other launch ended rc 0 with the completion marker
and no compiler error.

| Case | Trace | Reading |
|------|-------|---------|
| `border-pass`, `border-plain` | identical traces. Every crossing of a pad wall, in either direction and diagonally, prints `ENTER` or `LEAVE`, then `N` in the same cycle, with `HERE.SKILL1` already the new region. `MY.DISTANCE` 4–5.2, positions within one step of the wall. No `F` | a wall between two regions is passed whether or not it is `PASSABLE`, and each crossing fires its `IF_NEAR` |
| `border-high` | the same trace as border-pass: the player walks onto the pad with its floor at 14 | a 14-step floor rise does not block the player in this setup |
| `border-ceil` | the player walks onto the pad with its floor at the ceiling (16), with `ENTER`/`LEAVE` and an `N` at every crossing. `PLAYER_Z` reads 15.650 throughout | no region border that the player cannot cross was found; contact with one is still unmeasured |
| `liber` | `L` (`LIBER` thing) and `B` (`LIBER` actor) in every stand, 15 lines per 32 ticks. `T` only 6 from the plain thing, `A` only 6 from the plain actor | `LIBER` lifts the `EACH_TICK` gate for things and actors |
| `berkeley` | the plain thing `C` in every stand. `B` and `D` (`BERKELEY`) not while looking away, and not after turning to face them; `B` only 3 from the thing, `D` only 3 from the actor. No `N` or `F` | a `BERKELEY` object's `EACH_TICK` runs only while the player is within its `DIST`; it fired no `IF_NEAR`/`IF_FAR` |
| `berkeley-front` | `PLAYER_ANGLE` 0, then 3.141, then 0. `C` in every stand. `B` not while the thing was in front of the player (S0, S2), nor turned away; only 3 from it (S3). No `N`/`F` | being in view does not wake a `BERKELEY` object in this runtime; only `DIST` does |
| `clip-near` plain thing | `T` only while the player was within 10. `N` once at x 14.344, `MY.DISTANCE` 9.691: in the first cycle within the border, not at 15. No `F` on leaving the border or `DIST`. Its `EACH_CYCLE` count rose 0 → 3 only while the player was within the border | the `DIST` test, `IF_NEAR`/`IF_FAR` and `EACH_CYCLE` of a plain thing run only within its border. Leaving the border fires nothing |
| `clip-near` `LIBER` thing | `LN` at 14.461 (the first cycle within `DIST` 15), `LF` at 16.113. Its `EACH_CYCLE` count rose steadily, 1 → 55, through the whole run | a `LIBER` thing's `DIST` test and `EACH_CYCLE` run at any distance |
| `clip-near` plain actor | as the plain thing: `NA` at 9.691, no `FA`, `EACH_CYCLE` 0 → 3 only within 10 | actors are gated the same way |
| `clip-near` `MY.DISTANCE` | the `LIBER` thing's `MY.DISTANCE` read 65535.999 until the player first came within its `DIST`; after that it read |dx| + |dy| at any distance (e.g. 40.000) | an object's `MY.DISTANCE` starts at a large sentinel; when it is first refreshed is not pinned down |
| `clip-move` | for 160 ticks both actors stayed at their start (52 and 42 away). The FOLLOW actor moved 40 → 35.22 in the three cycles after the player was beamed 9 away, then stayed at 35.22 while the player walked off. The way actor moved 30 → 37.97 while the player stood 6 from its start, stopped once more than 10 away, and moved on (42.75, 44.34, turning back) after the player was beamed to (43, 8) | an actor outside its border does not move, and moves again within it. A `TARGET` naming a way makes the actor follow it |
| `clip-move-liber` | both actors moved from the first cycles on: the FOLLOW actor toward the origin, the way actor 30 → 46 and back | `LIBER` lifts the movement gate |
| `contact-thing`, `contact-actor` | identical traces. `DIST 0`: `N0` in each of the 4 consecutive pushing cycles (`pe_n` 7–10), the player held 3.21 from the centre and sliding slightly in y; none while standing against it or backing off; no `F0`. `DIST 2`: the same (`N2`, no `F2`). `DIST 8`: `N8` once at 6.820 on the way in, nothing while pushing, `F8` at 8.289 on the way out | pushing against a solid thing or actor fires its `IF_NEAR` in every pushing cycle while the player is outside `DIST`, with no `IF_FAR`. Within `DIST` contact fires nothing |
| `clip-regions` (room 10, pad 30) | `HERE.SKILL1` 2 in the pad. In the room: room objects at 8 run, the pad thing at 20 does not. In the pad: only the pad thing (8) runs; the room thing (24) and the room wall (16) do not. Just outside the pad: nothing (16, 12, 12) | the pad's 30 never applied |
| `clip-regions-order` | the same as clip-regions | the keyword order is not the cause |
| `clip-regions-walk` | walking in and out gives the same pattern as `LOCATE`. At (−5.95, 0.21) the room thing (10.26) does not run, the wall (9.84) does | the border is not taken from the last `LOCATE` |
| `clip-regions-swap` (room 30, pad 10) | at the origin the room thing and wall (16) run, the pad thing (24) does not. In the room at (−8, 0), the pad thing at 20 does not run. In the pad only the pad thing (8) runs; the room thing (24) and wall (16) do not. Back in the room the room objects at 16 and 12 run; the pad thing at 12 does not | room objects use 30 while the player is in the room and 10 while they are in the pad; the pad thing always uses 10 |

## Conclusion
Confirmed for patched V3.963:

1. **Crossing a wall between two regions fires its `IF_NEAR`**, as for a passable free-standing
   wall, whether or not the wall is `PASSABLE`. It fires in the same cycle as the region's
   `IF_ENTER`/`IF_LEAVE`, after them.
2. **Pushing against a solid thing or actor fires its `IF_NEAR` in every pushing cycle**, as long
   as the player is outside its `DIST`. Standing against it does not, no `IF_FAR` follows, and
   the `DIST` state does not change. Within `DIST` contact fires nothing.
3. **Outside its border a thing or actor runs no actions and does not move.** The border is
   |dx| + |dy| < `CLIP_DIST × THING_DIST` for things and `× ACTOR_DIST` for actors
   (OV-20261019-wall-pass-clip). Outside it the object does not run `EACH_TICK` or `EACH_CYCLE`,
   does not move toward its `TARGET`, and is not `DIST`-tested. It keeps its `DIST` state, so
   leaving the border fires no `IF_FAR`. Coming within the border while the player is within
   `DIST` fires `IF_NEAR` in that cycle.
4. **`LIBER` lifts all of rule 3**: `EACH_TICK`, `EACH_CYCLE`, movement and the `DIST` test run at
   any distance.
5. **`CLIP_DIST` is the smaller of the player's region's and the object's own region's.** A
   room wall followed the room's value like a room thing.
6. **A `BERKELEY` thing or actor is active only while the player is within its `DIST`**, whether
   or not it is in view. It fired neither `IF_NEAR` nor `IF_FAR`. (OV-20261019-actor-contact:
   a thing is near only while the eye is also within its `DIST` of the texture's centre; the eye at
   8 was 7 above these 2-high `DIST 4` objects, so the silence does not come from `BERKELEY`.)
7. **A `TARGET` naming a way assigns that way** (p.66): the actor patrols it.

## Confidence
high for rules 1–5 and 7: every case behaved the same way at every point it measured. Rule 5
fits all four region layouts, and rules 2 and 3 are the same for things and actors. medium for
rule 6: two cases, and the "seen" half of p.71 may need a real view (the runtime may never draw
an inactive object). The contact runs had only 4 pushing cycles at 8 ticks per cycle.

## Open Questions
- No region border that the player cannot cross was found: the player walked onto a floor 14
  above theirs, and onto a floor at the ceiling, from a floor-0 room with the default
  `PLAYER_CLIMB` (1.5, p.110). OpenVirtue blocks steps above 1.5 and regions with less than 3
  steps of headroom. What blocks a region change in the original, and whether contact with such
  a border fires `IF_NEAR`, is not measured. Answered by OV-20261019-border-climb: those pad walls
  were `PASSABLE`, which skips the height test.
- When the original refreshes a thing's `MY.DISTANCE` (65535.999 before the first refresh) is not
  measured. OpenVirtue computes it every cycle.
- Actor movement, `EACH_CYCLE` and the `DIST` test of walls outside `CLIP_DIST`, and walls between
  two regions with different `CLIP_DIST`s, were not probed. OpenVirtue uses the smaller of the
  player's region and both side regions for a wall's `EACH_TICK` only.
- Whether a gated object's texture animation pauses or only its `EACH_CYCLE` is skipped
  (OpenVirtue pauses it). `BERKELEY` walls (p.53) were not probed.
- Contact by an actor moving into a standing player (the 307 `IF_NEAR`s of
  OV-20261015-flic-scheduler-detail) is not modelled: OpenVirtue actors do not push the player.
  Answered by OV-20261019-actor-contact: an actor without `CAREFULLY` pushes the player and fires
  `IF_NEAR` in every pushing cycle. Gated texture animation (paused) and walls between regions
  with different `CLIP_DIST`s are answered by OV-20261019-clip-detail.

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_CrossingRegionBorderFiresNearWithoutPassable`.
- `WdlRuntimeTests.Tick_PlayerProximity_PushingAgainstThingOrActorFiresNearEveryCycleOutsideDist`.
- `WdlRuntimeTests.Tick_PlayerProximity_ThingOutsideClipDistIsNotDistTestedUnlessLiber`.
- `WdlRuntimeTests.Tick_ActorTargets_ActorOutsideClipDistDoesNotMoveUnlessLiber` (also a
  `TARGET` naming a way).
- `WdlRuntimeTests.Tick_ObjectEachCycle_ThingOutsideClipDistDoesNotCycle`.
- `WdlRuntimeTests.Tick_ObjectEachTick_ClipDistIsTheSmallerOfThePlayersAndTheObjectsRegion`.
- `WdlRuntimeTests.Tick_ObjectEachTick_BerkeleyThingRunsOnlyWithinDistAndFiresNoProximityHooks`.
- `PlayerTests.MoveHorizontal_StepEndingJustShortOfPortalLine_StillCrossesOnTheNextStep`.

## Implementation Notes
`LevelCollisionWorld.Move` reports touched objects, not only walls: a solid thing or actor that
pushed the mover back is added like a solid wall. `Player.TakeTouchedObjects` hands them to
`WdlRuntime.DispatchProximityEvents`, which fires `IF_NEAR` for any touched object while the
player is outside its `DIST`. The dispatch skips things and actors outside their border, keeping
their state, and never fires a `BERKELEY` object's hooks.

`WdlRuntime.IsWithinClipDistance` takes `CLIP_DIST` as the smaller of the player's region's and
the object's region's (a wall's two side regions). It returns false for a `BERKELEY` thing or
actor while the player is outside its `DIST`. A `LIBER` object is always within. Besides the
object `EACH_TICK` loop, it now gates the thing and actor texture animation (with its
`EACH_CYCLE`), the `DIST` test and actor movement in `AdvanceActorTargets`. `AdvanceActorTargets`
also treats a `TARGET` that names a way like the `WAY` property.

Writing the region-border test exposed a portal-crossing bug in `LevelCollisionWorld`. A step
that ended within float rounding of a portal line, still on the old side, made the next step
start "on the line", and that step was never counted as a crossing. The player then walked into
the other region without changing region. `CrossesPortal` now counts a step that starts on the
line and ends clearly off it, and the caller still changes region only when the end lies on the
other region's side.

## Addendum 2026-11-02: a free-standing wall keeps its region's outline; the LOCATE cycle gates by the region before

Carriers (original traces, sha256 first 8): `clip-regions` (`OVCCX0.TXT` `19ad9a2a`),
`rerun/clip-regions-walk` (`OVCCX0.TXT` `f907812c`), and from other notes
OV-20261019-clip-detail `wall-regions` (`OVCDX0.TXT` `80807bf5`), OV-20261019-eye-clamp
`stop-solid` (`OVECX0.TXT` `648dc091`), OV-20261020-view-activity `round2/long-wall`
(`OVVAX0.TXT` `0e1dbfca`) and `view-occlude` (`OVVAX0.TXT` `c83636bc`). No new runs; these
are re-read traces.

Each of these maps has a free-standing wall with the same region on both sides
(`WALL cr_wall 16 17 0 0` in `clip-regions`). OpenVirtue traced a region's outline only when
every vertex of its walls had exactly two neighbours, so the wall's two dangling ends left the
room without an outline. `LOCATE` then fell through to the outer `solid` region, which shares
the room's perimeter (`CLIP_DIST` 0, `SKILL1` 0): the original's `TK` after `R1L` reads
`HERE.SKILL1` 1, OpenVirtue's read 0, and no room object ran again. In `stop-solid` the player
then stood at the solid region's floor 16 (`AS` `PLAYER_Z` 15.650 against the original's 8) and
walked through the wall; in `long-wall` and `view-occlude` the actors froze at the 0 border.

The `clip-regions` trace also shows the cycle of a `LOCATE` into another region. At TICKS 88,
after `P1L` (room, `CLIP_DIST` 10, to the pad, 30), the room's thing and wall at 8 run once
more and the pad's thing does not; `TK` already reads `HERE.SKILL1` 2 in that cycle, and from 96
only the pad's thing runs. `wall-regions` agrees: the room wall at 26 runs at 120 right after
`P1L` into its pad (10) and then never again, and after `R3L` back to the room (152) it waits
for the next cycle (160). At `R2L` (120, pad to room) `clip-regions` prints the pad's thing once
more.

Rules:

1. **A wall with the same region on both sides is not part of that region's boundary.** It
   stands inside the region; the outline is traced from the other walls.
2. **In the cycle of a script `LOCATE` that moves the player to another region, the object pass
   still gates by the region the player had before**, at the position the player object still
   holds (the host moves it after the cycle). `HERE` and the region events change at once.

Results (oracle diff, all carriers): matched lines 23968 → 24099 over 623 carriers, no file
worse, no new full match. `clip-regions` 6 → 27 of 43 lines, `clip-regions-walk` 3 → 35 of
141, `wall-regions` 10 → 28 of 39, `long-wall` 19 → 43 of 67, `view-occlude` 25 → 61 of 220.
`stop-solid` now prints the original's `AS` 8/0/0 and stops at the wall, but its lines still
differ by the clock. The remaining `clip-regions` and `wall-regions` differences start where
the replay clock loses a cycle in a stretch without object prints (pe_n 15 against 16 at TICKS
144), a harness timing matter; the wall gate change of OV-20261019-clip-detail's addendum of the
same date accounts for 7 of the `clip-regions` lines (the counts above include it).

Tests: `LevelTopologyTests.LocateRegion_RoomWithAFreeStandingWall_FindsTheRoomNotTheRegionAroundIt`,
`LevelTopologyTests.TraceRegionLoop_RegionWithAFreeStandingWall_TracesItsBoundary`,
`WdlRuntimeTests.Tick_ObjectEachTick_CycleOfAPlayerLocateIsGatedByTheRegionBefore`.
`LevelTopology` skips such walls in `TraceRegionLoop` and `LargestLoop` (`BoundsRegion`), so
`LOCATE`, actor relocation and the floor mesh all get the outline. `WdlRuntime.Locate` records
the player's previous region and position; `IsWithinClipDistance` reads it through
`ClipGateRegion` until the end of the object pass.

## Addendum: hook order, the LIBER sentinel and the actor's refresh (2026-11-02)
Read from the existing traces of clip-near (`OVCCX0.TXT`, sha256 `c234835f`) and clip-move
(`OVCCX0.TXT`, `a8c8688c`). No new run.

- clip-near: in the cycle the player comes within the actor's `DIST`, the actor's `EACH_TICK`
  line (`A`) comes before its `IF_NEAR` line (`NA`), and both before the things' lines (`L`). A
  thing's `IF_NEAR` also follows its own `EACH_TICK`. So the `DIST` edge fires in the object's
  own turn of the object pass, right after its `EACH_TICK`, not in a pass before the objects.
- clip-near: the `LIBER` thing `cn_l` (`DIST` 15, `CLIP_DIST` 10) reads 65535.999 until the
  player first comes within 15 = 1.5 × its border (cycle 49, 14.461), although it is
  `DIST`-tested at any range. The `DIST` test does not write the distance it compares with into
  `DISTANCE` before the distance has started.
- clip-move: an actor's `DISTANCE` shows its move only from the next cycle. The FOLLOW actor read
  9.000 in the cycle that took it to 7.406, and 7.406 in the next. Only a move that ends in front
  of the player and within the `CLIP_DIST × ACTOR_DIST` border (|dx| + |dy| < 10) triggers that
  refresh. The WAY actor's move to 39.563 ended 11.563 away and refreshed nothing (W stays 9.969).
- clip-move: the border gate reads the refreshed distance too. The WAY actor, 11.563 away after its
  move but at a `DISTANCE` of 9.969, still ran its `EACH_TICK` and moved in the next cycle (orig
  TK(46) at x = 42.750).

Implemented: `DispatchDistEdge` runs in `RunObjectPass` after each object's `EACH_TICK`.
`TryCurrentProximity` writes `DISTANCE` only for a wall or a started thing or actor.
`NoteFrontMove` also requires the end of the move to lie within `ClipBorder`, the forced in-cycle
refresh after the actors' moves and on a `DISTANCE` read is gone, and `IsWithinClipDistance`
compares with `TryDistTestDistance`. Oracle diff: clip-move 27/56 → 56/56 (full match), clip-near
119/208 → 175/208 and `OVSTA0` 1/3 → 3/3. The rest of clip-near is a thing's `EACH_CYCLE` count
and a 9.562/9.563 print rounding, both outside this rule. Over the whole diff, 291 → 294 full
carriers and no file got worse. Tests: `Tick_PlayerProximity_DistEdgeFiresInTheObjectsTurnAfterItsEachTick`,
`Tick_PlayerProximity_LiberThingKeepsTheDistanceSentinelUntilItsDistanceStarts`,
`Tick_ObjectEachTick_ActorClipBorderReadsTheRefreshedDistance`, and
`Tick_EachTickActions_ReadDistanceRefreshedAtCycleStart` (which replaces
`..._ReadDistanceFromCurrentPlayerAndMovedActorPositions`).

## Replay notes (2026-11-02)
- **Under `SKIP_FRAMES` an actor beyond its border counts as not drawn.** clip-move-liber (trace
  `OVCCX0.TXT`, sha256 `b603126a`): the `LIBER` `FOLLOW` and way actors start 52 and 42 from the
  player (`CLIP_DIST` 10), both inside the view cone, and both move in bursts of six cycles'
  distance (|dx| + |dy| 9.561 = 6 × 1.594 between `TK(1)` and `TK(4)`, nothing between `TK(4)` and
  `TK(7)`, and so on). OpenVirtue counted them as visible and moved them every cycle. Rule: a thing
  or actor at or beyond its `CLIP_DIST` border (`LIBER` ignored) is not drawn, so `SKIP_FRAMES`
  treats it as unseen even inside the view cone. `VISIBLE` beyond the border is not measured and
  is left as it was. Result: `OVCCX0.TXT` matches 64 of 159 lines (was 56); the bursts now match up
  to the beam to (31, −12) in probe cycle 21, after which the `FOLLOW` actor's burst in OpenVirtue
  still heads for the old player position (not resolved here). No other carrier changes.
  `WdlRuntime.SkipFramesMultiplier` and `IsBeyondClipBorder`; test
  `WdlViewActivityTests.Tick_SkipFrames_ActorInViewBeyondClipDistCountsAsUnseen`.

## Replay notes (2026-11-03, harness)
- **The lost cycle was the replay's clock.** clip-regions (`OVCCX0.TXT`, sha256 `19ad9a2a`) ran at
  8 ticks a frame throughout: every `TICKS` it prints is a multiple of 8. The replay now runs such
  a launch at fixed 8-tick frames instead of filling frames between sparse samples, and
  clip-regions matches in full (43/43, was 27), with OV-20261019-clip-detail wall-regions. The
  rerun clip-regions-order (`74b97e4c`) lost its first `TK` line (13.500) to a different start-up
  step; that line comes 31 ticks before the first `T` sample, where no sample records the frames.

## Addendum: a BERKELEY object's one run in the first cycle (2026-11-04)
Re-read of rerun berkeley-front (`OVCCX0.TXT`) against round 1's berkeley, no new run. Both put a
`BERKELEY` thing (`DIST 4`, `EACH_TICK` counting its runs, printing every two ticks) 10 from the
player at the origin: berkeley-front's ahead at (10, 0), round 1's behind at (−10, 0). Both beam
the player to within 3 of it late in the run, and from then both count a run every cycle. The
counts differ by one: berkeley-front's thing has run once before the beam, round 1's not at all,
and berkeley-front's trace has no `B` line for that run. Its `EACH_TICK` prints only while
`TICKS > kb_t`, with `kb_t` 0 at first, so a run at `TICKS` 0 prints nothing: the run was in the
engine's first cycle, before any frame was drawn, when the object stood in the player's view.
- **Rule.** In the first cycle a `BERKELEY` thing or actor ahead of the player within the view's
  arc runs its `EACH_TICK` once, outside its `DIST` as well; from the second cycle on only within
  `DIST`, as before. The view is geometric there (no frame yet): the bearing within
  `PLAYER_ARC`/2 of `PLAYER_ANGLE`.
- **What it is not.** The gate in the cycle of the beam itself still reads the distance kept from
  the cycle's start (the thing did not run in the beam cycle: no third run), as the `LOCATE`
  addendum found for `CLIP_DIST`.
- Result: berkeley-front matches in full (115 lines) and round 1's berkeley still does; 11 of 16
  carriers. `WdlRuntime.IsWithinClipDistance`, `IsWithinViewCone`, `_inFirstCycle`; test
  `Tick_ObjectEachTick_BerkeleyThingRunsOnlyWithinDistAndAnEyeHighAboveItFiresNoHooks`.

## Rounds 2 and 3: the far `DISTANCE` of `LIBER` objects, the object order, the near range (2026-11-04)
clip-move-liber's `LIBER` way actor read a `DISTANCE` of 29.438 in the cycle after the first beam,
three times its border 10 and twice the 1.5 × border at which a thing starts (clip-near), then the
sentinel 65535.999 again at 39–41 while the player walked away, 11.563 after the next beam, and
46.844 real after the last; its `FOLLOW` actor read the sentinel at 39.3 after one beam and 40.719
real after another. Seven new carriers under `round2/` and `round3/` (fixture generator
`ClipContactProbeCases`, cases `liber-far-*`; all ran fast, about 0.26 ticks a cycle, with
`SDL_VIDEODRIVER=dummy`), each in the `CLIP_DIST 10` room with `LIBER` objects at (36, 12) and
(10, 12) and the player beamed to thirteen stands (|depth| + |lateral| from the first pair, then the
second: the origin 48, 22; P1 (31, −12) 29, 45; P2 (31, 0) 17, 33; P3 (31, 6) 11, 27; P4 (19, −12)
41, 33; P5 (23, −12) 37, 37; P6 (15, −12) 45, 29; P7 (30, −8) 26, 40 behind; P8 (36, −2) 14, 40
behind; P9 (12, 8) 28, 6; P10 and P11 as P7 and P8; P12 the origin; P13 as P4), then walks:

| Carrier (`OVCCX0.TXT` sha256) | Objects | Reading |
|------|---------|---------|
| liber-far-beam (`round2/`) | a standing actor and a thing at each spot | all four 65535.999 through P2; the first pair 11 from P3 on and then 41, 37, 45, 26, 14, 28, 26, 14, 48, 41 — real at every later stand, 48 included; the second pair 65535.999 through P8 (40 behind at P7 and P8 included), 6 from P9 and then 40, 40, 22, 33 |
| liber-far-walk (`round2/`) | the same | started at 11, then 29 after the beam to (31, −12) and 29.5 … 47.7 a cycle as the player walked off to x = 11.5, back down to 27.8; the second pair 40 and 40.9 … 47.3 walking away, 31.2 walking back |
| liber-far-follow (`round3/`) | `FOLLOW` actors of `SPEED 0` in place of the standing actors | identical to the standing actors in every stand and the walk |
| liber-far-way (`round3/`) | way actors patrolling half a step in place ((36, 12) ↔ (36.5, 12)) | 29.3 and 44.7 at P1, the first refresh after the first beam, where the things beside them stayed at 65535.999; real at every later stand (41.2, 45.2, 48.2, 40 behind) and through the walks |
| liber-far-contact (`round3/`) | two `FOLLOW` actors at 0.2 a tick from (40, ±12), the player beamed between contact and far stands | the one 12.6 away started at the first beam, the other at 26.6; in contact both push the player diagonally about 0.25 a cycle (the second stand drifted (31, −12) → (46, −22)) |
| clip-move-liber-nexus60 (`round3/`) | round 1's clip-move-liber with `NEXUS 60` in place of 14 | the way actor's sentinel returns at 45.6 while the follower is 4.3 in front, as with `NEXUS 14` at 41.4: not the nexus |

- **A standing thing or actor starts its `DISTANCE` only when first refreshed within 1.5 × its
  border, and then keeps reading its distance at any range** (48 at P12 after a start at 11; 40
  behind at P10 after a start at 6). A `FOLLOW` actor that cannot move (`SPEED 0`) counts as
  standing. This is rule 2's start (OV-20261019-actor-contact) confirmed at every range and
  behind the player.
- **An actor that has moved reads its distance from the next refresh however far it is.** The
  half-step way actors read 29.3 and 44.7 at the very first refresh; clip-move-liber's follower
  started at 26.6. A move is enough, a beam is not: the standing pair beside them stayed at the
  sentinel through the same beams.
- **The far sentinel of a moved actor is open.** Only clip-move-liber (and its `NEXUS 60` rerun)
  shows it, always while the other actor stands within about 9 in front of the player (drawn), with
  the far one at 39 or more; the in-place way actors, with nothing drawn, never fell back, and
  liber-far-contact's far follower stayed real at 17 while the near one was drawn 3 in front. Not
  a function of the far one's position alone (41.4 sentinel and 46.8 real from the same room), not
  the nexus. Not implemented; the two carriers keep their lines.
- **Things and actors run from the last placed to the first whatever their kind.** liber-far-beam
  placed actor, thing, actor, thing and ran thing, actor, thing, actor (`U B T A`) from its first
  cycle. scheduler-order's actors ran before its things because its map placed them after
  (OV-20261029-scheduler-order order and order-wmp both do).
- **The `SKIP_FRAMES` near range stops at the border.** clip-move-liber's follower, refreshed to
  10.123 after the beam (border 10), took no step until the next refresh read 6.119 and then
  covered the three cycles since its last move; clip-move's way actor at a `DISTANCE` of 9.969
  still stepped. So an unseen actor moves every cycle while its refreshed distance is both within
  20 (OV-20261020-view-activity, measured in rooms of the default `CLIP_DIST`) and within its
  `CLIP_DIST × ACTOR_DIST` border.
- **A `FOLLOW` actor's target is the player as the skills read it.** In the cycle of the first beam
  clip-move-liber's follower, skipped out of view, took its six-cycle step from (17.9, −5.4) to
  (24.3, −8.6), toward the beamed (31, −12) that `PLAYER_X`/`PLAYER_Y` already held, not toward the
  origin where the player's body still stood.
- **A long step into the player does not push.** In clip-move-liber the follower's three-cycle step
  at the walk's second cycle ended 0.95 from the player's centre and moved the player nothing; the
  player's own step that cycle was lost (27.000 printed twice) and the next went on. OpenVirtue
  pushes the player out by the contact rule (OV-20261019-actor-contact) and the player walks on.
  Open with push-wall's pw05-k2 (the step beyond the contact distance).

Implemented: `WdlRuntime.NoteFrontMove` starts a moved actor's distance (`_distanceStarted`),
`RunObjectPass` orders the placed things and actors by `Level.MapObjects` reversed (the unplaced
ones first, actors before things, as before), `IsWithinSkipFramesNearRange` adds the border, and
`AdvanceActorRelativeToPlayer` reads `PLAYER_X`/`PLAYER_Y`. Tests
`Tick_PlayerProximity_AMovedActorStartsItsDistanceAtAnyRange`,
`Tick_SkipFrames_TheNearRangeStopsAtTheClipBorder`,
`WdlSchedulerOrderTests.Tick_RunsPlacedThingsAndActorsTogetherFromTheLastPlaced`. Oracle diff:
liber-far-beam, liber-far-way and liber-far-follow match in full, liber-far-walk 764 of 770 (the
actors' start-up positions), clip-move-liber 93 of 159 (was 61; the rest the far sentinel and the
long step's push), liber-far-contact 226 of 1096 (the pushes of two followers in contact; open).
The replay harness dates the start-up from the start-up file and the resumed start action's first
lines (tools/README): these fast launches began with a 5/32 first frame where the cadence run back
put 13/32.

## Rounds 4 and 5: a follower's long step into the player (2026-11-04)
clip-move-liber's follower, lifted from skipping, took a three-cycle step that ended 0.95 from
the player's centre and moved the player nothing; the player's own 4-step walk that cycle was
lost. Eight carriers under `round4/` and `round5/` (cases `long-step-*`, fast launches): a 2-wide
`LIBER` `FOLLOW` actor east of the player at the origin, beyond the border at `SPEED 0`, given
`SPEED 6` by the probe so that its first move covers the cycles since its last (9 or 10.125 at
0.25 or 0.28125 a cycle); a per-cycle sampler (`P`) prints the player, the actor and its
`DISTANCE`.

| Carrier | Reading |
|------|---------|
| long-step-stand (10.5), -deep-101, -deep-104, -deep-110 (11) | the long step ends 1.5, 1.1, 1.4, 2.0 from the standing player and pushes it to 2.22 from the actor; the single steps after push it on, 2.22 ahead each cycle, the actor stepping to 0.54 from it before each push; at the west wall the push is cut (see below) |
| long-step-shallow (12.5) | the long step ends 2.375 away, outside contact: no push; the single steps push as actor-push |
| long-step-edge (11.7) | the long step ends 1.575 away, just inside: pushed to 2.22 |
| long-step-walk | the player walking −X at 0.5 a tick (0.13 a cycle) away from the actor reads the push's position exactly in every cycle (−0.720, −2.406 …): its own step is overridden, and the push's lateral part is 0.005 where the standing player's is 0.004 — the push is taken from where the step (with its 4/1024 nudge aside) ended |
| long-step-into | the player walking +X at 15 a tick (3.75 a cycle) into the actor from 14: cycle 5 the long step to 5.0 and the walk stopped at contact (2.779); cycle 6 the actor steps to 0.54 from the player, which moves nothing; cycle 7 the actor steps onto the player's centre and the player walks 3.75 straight through; then the actor follows 2.22 behind; at the east wall the player stops alternating 46.751/46.755 and drifting aside the cycle the actor steps into it from behind, and stays so with the actor at rest on its centre |

- **The push is the player's own motion.** An actor's step into the player pushes nothing in the
  actor's turn: in the player's motion phase a step of the player's toward the actor is held
  whole (clip-move-liber cycle 24, long-step-into cycle 6), any other step is taken and the player
  is then pushed out to the push distance from where it ended (long-step-walk's 0.005 aside), and
  the actor's `IF_NEAR` fires after that. A push a wall refuses, or one that moves nothing (the
  player on the actor's centre within the 22.10 root's reach), puts the player back where the
  cycle found it, its own step and the wall's 4/1024 nudge undone (long-step-into at the east wall).
- **From the actor's centre the player walks freely** (long-step-into cycle 7, clip-move-liber
  cycle 25), but a step that ends on a solid actor's centre is undone (long-step-into cycles 38 on).
- **A long step is no different**: it pushes from 1.1 as from 2.0 (deep-101, -110) and the next
  single step pushes from 0.54 (every stand), so the depth of the step does not matter; what lost
  clip-move-liber's push was the player's walk toward its follower.
- **A push a wall stops lands half way to the wall's line.** With the player 2.761, 2.362 and
  2.662 from the west wall and the actor's step ending 0.54 behind it, the original moved the
  player 0.756, 0.556 and 0.706 toward the wall: to 2.005, 1.806 and 1.956 from it, each exactly
  the midpoint between where the cycle found it and the walk's wall line 1.249 from the wall
  (`PLAYER_WIDTH` 1.2 plus the clearance 50/1024), the lateral part the midpoint's too (0.050 →
  0.056 with the held end at 0.062). proximity-events near-actor's small actor pushed its player
  from 2.634 to 1.941 the same way (0.693). Within about the actor's radius of the line the push
  is refused whole instead: long-step-edge's player at 1.354 (0.105 from the line) and
  long-step-into's on the line kept their place and their lateral part. The bound is a guess
  between 0.105 and 1.113. actor-push's three cycles at its wall (no move at 2.78, then 0.31 and
  0.72, then rest at 1.751 with the 4-wide actor stopped 1.06 behind) follow neither and stay open.
- **Open: the lateral nudge after a step aside.** long-step-walk's player, 4/1024 aside from its
  step's nudge, reads 5 after the push (the lateral part scaled to the push distance, 5.4) where
  the 4/1024 minimum on a part under 4 (OV-20261029-actor-step, actor-push) gives 8; a minimum on a
  part that rounds to 0 fits this but breaks deep-110 and the standing pushes. Left at 3 units.

Implemented: `WdlRuntime.MoveActor` records the actors that stepped into the player
(`_pendingPushers`); `WdlPlayerMotion.Apply` asks `IsHeldByActorPush` before the step and
`ResolveActorPushes` after it (the push from the stepped position, the wall-refused or nil push
restoring the cycle's start, the step ending on a centre undone, `IF_NEAR` after);
`LevelCollisionWorld.Move` holds a move that starts inside an actor's contact circle and heads
toward it, and ignores that actor otherwise; `Player.PushAway` lands a wall-stopped push midway
from the cycle's start, or refuses it within the actor's radius of the line. Tests
`Apply_PlayerStepTowardAnActorInsideIt_IsHeld`,
`Apply_PlayerStepAwayFromAnActorInsideIt_IsPushedFromWhereTheStepEnds`,
`Apply_PushAWallStops_NearTheWallIsRefusedWhole`, `Apply_PushAWallStops_FartherOutLandsMidway`.
Oracle diff: seven of the eight carriers match in full, long-step-walk 7 of 54 (the lateral
residual from its first push); clip-move-liber 140 of 159 (was 93; the rest the far sentinel),
near-actor 17 of 24 (was 13); actor-contact, contact-clock and overlap unchanged.


## Rounds 6 to 10: the far sentinel is the unstarted state again (2026-11-04)
Rounds 2 and 3 left the far sentinel of clip-move-liber's moved actors open: 65535.999 at 39 or
more while the follower stood within about 9 in front of the player, real at 29–37 and after
later beams. Ten carriers under `round6/` to `round10/` (cases `far-drawn-*`, `far-plain-way`,
`far-front-*`; fast launches, about 0.26 ticks a cycle) in the `CLIP_DIST 10` room, each with
`LIBER` objects at (36, 12) — a half-step way actor `A`, a standing actor `S`, a thing `T` — started
by a stand at P3 (31, 6), 11 away, then beamed to a stand 36 away and to the origin (48):

| Carrier | What stands near the player | Reading of the far objects |
|------|---------|---------|
| far-drawn-stand, far-drawn-actor (`round6/`, `round7/`) | a thing, or a standing actor, 3 to 9 in front, drawn | real at 37 … 48 through every stand |
| far-drawn-way, far-plain-way (`round6/`) | the same with the far object a way actor, or nothing drawn | real throughout |
| far-front-mover (`round8/`) | a `FOLLOW` actor of `SPEED 0.1` at (26, −12); the player beamed to (24.2, −12), 1.8 from it, inside its contact circle, and pushed west and south about 0.04 a cycle for 48 ticks | `A`, `S`, `T` 65535.999 from the first line after the beam (35.8 away) and still at the origin (48); the follower real while pushing (3.73 → 2.70), 65535.999 at the origin (38) |
| far-front-spread (`round9/`) | the same with the far objects three apart | the same: not the stacking |
| far-front-nof (`round9/`) | the same beams with no follower | real: 35.8 and 36.1 at the stand, 48 at the origin |
| far-front-approach (`round9/`) | the player beamed to (31, −12), 9.15 from the follower, which never stepped | real: 29.3 and 48.2; the follower 38.4 real at the origin |
| far-front-step (`round10/`) | the player beamed to (29, −12), 7.17 from the follower, which stood 40 ticks and then took a catch-up step to 3.11 and pushed the player east and south every cycle | real throughout: 31.0 … 30.7 while pushed, 48 at the origin; the follower 39.96 real at the origin |
| far-front-restart (`round10/`) | far-front-mover's run, then P3 (31, 6) again and the origin once more | 65535.999 through the stand and the origin, then `T` 11.000 at P3 and 48.000 at the second origin; the follower 65535.999 at the first origin (38), 26.48 at P3 (standing), 32.68 at the second origin |

- **The sentinel is the unstarted state again.** A far object that reads 65535.999 restarts
  exactly as at launch: a refresh within 1.5 × its border (`T` 11 at P3) and then its distance at
  any range (48 at the second origin). The follower, which had moved, restarted at 26.48 in the
  cycle after the beam with no step of its own, as the moved actors of rounds 2 and 3 did.
- **It is not a drawn sprite, a pushing actor or a stacked spot.** A thing or a standing actor
  drawn 3 to 9 in front left the far objects real (far-drawn-*); a follower stepping to the player
  and pushing it every cycle left them real (far-front-step, 31 away); three objects on one spot
  read as three apart.
- **What un-starts them: a beam that lands inside a follower's contact circle, and a beam out of
  it.** far-front-mover's beam to 1.8 from the follower un-started everything 36 away at once
  (the follower itself, 1.8 away, stayed started), where the same beam without the follower, to
  9.15 or to 7.17 from it changed nothing; the beam from contact to the origin un-started the
  follower (38) where the beam from 9.15 or 7.17 left it real at 38.4 and 39.96. clip-move-liber's
  beam from contact to W1 un-started its follower (39.3) the same way, and its way actor, 11.6
  away, restarted at once. clip-move-liber's way actor going to the sentinel at 39–41 (45.6 in
  the `NEXUS 60` rerun) while the player walked away from its follower stepping 4–6 behind, with no
  beam, is the one reading these carriers do not reach.
- Not implemented: the beam-with-contact rule would fit far-front-mover, -spread and -restart and
  clip-move-liber's follower after W1, and nothing shows what it would do to liber-far-contact's
  beams between contact and far stands. The sentinel lines stay as they are; everything else in
  the ten carriers replays, up to the follower's launch-time step (24.039 … 24.053 against 24.046,
  the clock family) — far-drawn-* 547 of 548 (a timing line), far-front-nof 183 of 202 (the way
  actor's 48.201 against 48.164), far-front-approach 198 of 263, far-front-step 177 of 296.
