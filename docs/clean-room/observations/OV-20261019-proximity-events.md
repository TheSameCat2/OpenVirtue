# OV-20261019-proximity-events: When IF_NEAR, IF_FAR, IF_ENTER and IF_LEAVE fire
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
The no-flic controls of OV-20261015-flic-scheduler-detail left two side observations:

1. Beaming the player into another region with `SET PLAYER_X/PLAYER_Y` never fired that region's
   `IF_ENTER`, but walking with `PLAYER_VX/PLAYER_VY` did. Is a beam ever picked up later, by the
   next walk or by `LOCATE` (p.89)?
2. A FOLLOW actor's `IF_NEAR` ran in 307 consecutive cycles while it pushed the player. Does
   `IF_NEAR` run in every cycle the player is within `DIST`, or once per crossing? Does `IF_FAR`
   fire, and do beams and actor movement count?

## Why It Matters
OpenVirtue fired region events on any change of the player's region, and fired `IF_NEAR`/`IF_FAR`
once per crossing of `DIST`, measured as a straight-line distance. The retail scripts arm
`IF_NEAR` on pickups, enemies (wake-up) and some decorations, and `IF_FAR` on doors that lower
again and on thrown pickups (re-arming `IF_NEAR`). Most of these `IF_NEAR` actions clear the hook
themselves. Some exits do not: a health, armour or ammo pickup at full value, and the
"inventory full" message, end the action and leave the hook armed. Whether the hook repeats
decides whether such a pickup is collected later while the player stands on it. These are facts
read from the scripts' structure, not their text.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.17 (WMP `WALL` line), p.51–52 (wall `DIST`,
  `#DISTANCE`, `IF_NEAR`, `IF_FAR`), p.58 (region `IF_ENTER`/`IF_LEAVE`), p.68–69 (object `DIST`,
  `#DISTANCE`, `IF_NEAR`, `IF_FAR`), p.89 (`LOCATE`), p.99 (`HERE`), p.111 (`PLAYER_VX`). The
  retail scripts were read (structure only, not copied) for which hooks they use. The retail WMP
  files were scanned for which side of a wall each placed thing's region lies on.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-proximity-events/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261015-flic-scheduler-detail).
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. The first
  carrier (`near-walk`) ran at 61 frames per second (`TIME_CORR` 0.25). The other four ran at
  8 ticks per cycle. Their readings do not depend on the frame rate.
- Fixture generator: `ovtool oracle prepare proximity-events --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/ProximityProbeCases.cs`). The player starts at (0, 0) looking along
  +X. A global `EACH_TICK.1` sampler counts its runs in `pe_n` and sets `PLAYER_VX/PLAYER_VY` from
  two skills every cycle, so the run action walks the player by setting them. Every trace line is
  a token, `TICKS` and `pe_n`, then extra skills. `IF_NEAR`/`IF_FAR` actions print every run with
  `MY.DISTANCE` and the player position.
- Proximity objects use a texture 2 steps wide, so contact needs a centre distance under about
  2.2 steps, well inside their `DIST` of 10.
- Cases:
  - `near-walk`: a THING at (16, 6). The player walks along +X at 0.2 steps per tick to x ≈ 36,
    stands, walks back to x ≈ 16 and stands 200 ticks, then creeps 0.6 steps.
  - `near-beam`: the same THING. The player is beamed to (16, 0), stands, is beamed to (36, 0),
    stands, is beamed back, stands, creeps, and walks out along +X.
  - `near-actor`: a FOLLOW actor (`SPEED 0.2`, `DIST 10`) starts at (40, 0) and walks toward a
    player who stands still. The player then backs off along −X.
  - `region-beam-walk`: the pad island of the flic-detail probe (x −12…−4, y 12…20, invisible
    passable walls). The room has `SKILL1 1` and the pad `SKILL1 2`, and `HERE.SKILL1` is printed
    as `pe_h`. Both regions print `IF_ENTER`/`IF_LEAVE`. The player is beamed to (−8, 16) without
    `LOCATE`, creeps +X inside the pad, walks out across x = −4 to x ≈ 1.6, then walks back in.
  - `region-locate`: beam into the pad followed by `LOCATE`, beam out followed by `LOCATE`, then a
    beam in with `LOCATE` 32 ticks later.

## Observations
All runs 2026-10-19 13:50–13:55 UTC, five launches, one per case, all rc 0 with the completion
marker and no compiler error in `OVCON.TXT`.

| Case | Trace | Reading |
|------|-------|---------|
| `near-walk` | `N` once at player (11.194, 0.840), `MY.DISTANCE` 9.966. `F` once at (21.662, 1.625), 10.037. No line while standing outside. On the way back one `N` at (23.537, 3.570), 9.967. No line in 200 ticks standing at (15.996, 3.792), 6.2 from the thing, nor while creeping | `IF_NEAR` once on the way in, `IF_FAR` once on the way out. No repeat while within `DIST` |
| all `N`/`F` lines | `MY.DISTANCE` = \|dx\| + \|dy\| between player and thing: 4.806 + 5.160 = 9.966, 5.662 + 4.375 = 10.037, 7.537 + 2.430 = 9.967, 5.406 + 5.973 = 11.379; after the beam to (36, 0): 20 + 6 = 26.000 | the distance is the sum of the axis distances, and `DIST` is compared with it |
| `near-beam` | `B1D` (16, 0), then `N` 6.000 in the next cycle. No more `N` while standing. `B2D` (36, 0), then `F` 26.000. `B3D`, then `N` again. Nothing while creeping. `F` 11.379 while walking out | beams fire `IF_NEAR` and `IF_FAR` like walking does, once per crossing |
| `near-actor` | one `N` with `MY.DISTANCE` 9.520 while the player still stood at (0, 0). The actor then pushed the player to (−14.06, 0.06). No further `N` | actor movement is checked too, at `DIST`. Being pushed while within `DIST` does not repeat the hook |
| `region-beam-walk` | `B1D` (−8, 16) with `pe_h` 1, no event. Creeping inside the pad: `pe_h` 1, no event. Walking out across x = −4: no event, `pe_h` 1. Walking back in across x = −4: `RLEAVE` and `ENTER` with `pe_h` 2 | a beam keeps the region (`HERE`). Crossing a wall onto the side of the region already held changes nothing. Crossing onto the other region's side enters it |
| `region-locate` | `B1L`, then `RLEAVE` + `ENTER` (`pe_h` 2) in the same cycle, after the global slot. `B2L`, then `LEAVE` + `RENTER` (`pe_h` 1). `B3D` without `LOCATE`: nothing for 32 ticks. `L3`, then `RLEAVE` + `ENTER` | `LOCATE` assigns the region and fires the old region's `IF_LEAVE`, then the new one's `IF_ENTER`. `HERE` is already the new region inside `IF_LEAVE` |

The retail level maps (the four with placements) put the wall's first region on the left of v1 → v2 in the
map's y-up frame. Of 2,733 placed things and actors, 2,729 lie on the side of their nearest wall
that holds their own region under that rule. The pad walls of this probe follow the same rule
(`WALL pad_wall v1 v2 2 0` with the pad corners counter-clockwise). The manual names the two
fields "left and right regions (relative to the view from v1 to v2)" (p.17).

## Conclusion
Confirmed for patched V3.963:

1. **`IF_NEAR` fires once when the player comes within `DIST`, and `IF_FAR` once when they leave
   it.** Standing, creeping or being pushed within `DIST` does not repeat the hook. The test runs
   every cycle on the current positions, so walking, a beam and a moving actor all count.
2. **For things and actors, `DIST` is compared with |dx| + |dy|** between the player and the
   object's centre. `MY.DISTANCE` reads the same value. The manual calls it approximate (p.69).
3. **A write to `PLAYER_X/PLAYER_Y` keeps the player's region (`HERE`).** It fires no region
   event, and neither does later movement inside the new region.
4. **Crossing a wall puts the player in the region on the side moved onto**: the first WMP region
   on the left of v1 → v2 (y-up), the second on the right. With a stale region, crossing onto the
   side of the region already held changes nothing.
5. **`LOCATE` fires `IF_LEAVE` of the old region, then `IF_ENTER` of the new one**, in the same
   cycle after the global `EACH_TICK` slot.

The 307 consecutive `IF_NEAR` runs of OV-20261015-flic-scheduler-detail `world-control` are not a
per-cycle repeat of the `DIST` test. There the actor's `DIST` (6) was smaller than the contact
distance (about 9.2 steps for a 16-step texture), and `IF_NEAR` ran in every cycle the actor
pushed the player, starting at contact with |dx| + |dy| = 12.8. It stopped once the actor overlapped
a player who no longer moved (|dx| + |dy| = 0). Contact seems to fire `IF_NEAR` on its own
("at every contact", p.69). That case had no `IF_FAR`.

## Confidence
high for rules 1–5: each case matches one predicted reading. The |dx| + |dy| rule fits all seven
printed distances to three decimals. The side rule fits the probe and 99.9 % of the retail
placements. medium for the contact reading, which rests on one case.

## Open Questions
- Contact: does an actor (or the player pushing into a thing) fire `IF_NEAR` in every contact
  cycle when `DIST` is below the contact distance, and what does that do to `IF_FAR`? In
  flic-detail `near-control` the player was beamed 4 steps from a 16-step THING (`DIST` 6,
  `IF_FAR` set): `IF_NEAR` fired once, the player was pushed out to |dx| + |dy| = 9.2, and
  `IF_FAR` never fired, against rule 1. OpenVirtue has no contact rule yet.
- Walls: the manual measures a wall's `DIST` to its nearest vertex (p.51). Answered by
  OV-20261019-wall-distance: |dx| + |dy| to the nearer vertex.
- Order in the cycle: `LOCATE` events ran after the global `EACH_TICK` slot in the same cycle,
  and walking events after the sampler too. OpenVirtue dispatches them before the slot, and moves
  the player after the WDL cycle, so a walk's events come one cycle later than in the original.
- When the original refreshes `MY.DISTANCE` within a cycle, and what it reads outside the
  player's `CLIP_DIST` (p.69) or in another region. Not probed (see Implementation Notes).
- Whether `DIST` is compared with `<=` or `<` (no sample at exactly 10).

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_ComparesDistWithSumOfAxisDistancesAndFiresOncePerCrossing`:
  3.54 steps straight-line but 5 by axes is not within `DIST` 4; `IF_NEAR` fires once and does
  not repeat while the player moves within `DIST`; `IF_FAR` fires once.
- `WdlRuntimeTests.Tick_PlayerProximity_NearAndFarActionsReadDistanceAsSumOfAxisDistances` and
  `WdlRuntimeTests.Tick_EachTickActions_ReadDistanceFromCurrentPlayerAndMovedActorPositions`:
  `MY.DISTANCE` inside `IF_NEAR`, `IF_FAR` and `EACH_TICK` actions.
- `PlayerTests.MoveHorizontal_AfterBeamWithoutLocate_KeepsStaleRegionUntilCrossingOntoTheOtherSide`.
- `RuntimeHostRegionEventTests`: a beam keeps `PLAYER_REGION` and fires nothing; `LOCATE` then
  fires `IF_LEAVE` and `IF_ENTER` once.
- The portal maps of `PlayerTests` and `WdlPlayerMotionTests` listed the regions in the
  opposite order from rule 4 and were reversed.

## Implementation Notes
`WdlRuntime.TryProximityDistance` uses |dx| + |dy| for things and actors. Walls kept the segment
distance until OV-20261019-wall-distance. The edge-triggered dispatch in `WdlRuntime.DispatchProximityEvents` already matched rule
1. `LevelCollisionWorld.CrossPortal` takes the region on the side of the wall that the mover
crosses onto (`RegionOnSide`) instead of the wall's other region, so a stale region after a beam
behaves as in the original. Rules 3 and 5 already held: the host applies `PLAYER_X/PLAYER_Y`
writes without touching the player's region, and `LOCATE` sets it, which the next cycle's
`DispatchPlayerRegionEvents` reports.

`MY.DISTANCE` (added after the first implementation) is a skill of every thing and actor that
`WdlRuntime.RefreshDistances` sets to the same |dx| + |dy|. The probe only read it inside
`IF_NEAR`/`IF_FAR`, so these choices are OpenVirtue's own:
- It is refreshed twice per cycle: at the start, before resumed `WAIT` actions, and again after
  the actors move, before the global and object `EACH_TICK` actions. The `DIST` test also stores
  the value it compared, so an `IF_NEAR`/`IF_FAR` action reads exactly the distance that fired it.
- It is computed for every thing and actor, in any region and at any range, with or without a
  proximity hook. The manual only promises it within the player's `CLIP_DIST` (p.69).
- Flic cycles (`TickFlic`) do not refresh it: player and actor movement are paused there.
- A script write to it lasts until the next refresh.
- Walls got `DISTANCE` later, from OV-20261019-wall-distance (|dx| + |dy| to the nearer
  vertex, refreshed once at the start of the cycle).
