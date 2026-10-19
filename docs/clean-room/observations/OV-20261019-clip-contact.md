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
   or not it is in view. It fired neither `IF_NEAR` nor `IF_FAR`.
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
