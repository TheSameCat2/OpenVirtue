# OV-20261030-below-beam: where a player beamed into a BELOW stack lands
Date: 2026-10-30
Status: confirmed (two rounds) and implemented
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
OV-20261029-below-view left open where the original puts a player beamed into a `BELOW` stack
(p.59–61), and what it does when the eye is between a lower storey's ceiling and the storey above.

## Why It Matters
Saints of Virtue beams the player with `SET PLAYER_X/PLAYER_Y` and `LOCATE` (teleporters, level
starts), and its levels stack regions under bridges and water. OpenVirtue's `LOCATE` always chose
the stack's top storey. A player beamed into an underpass ended up on the deck, its eye lifted
through the slab.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: the 1998 A3 manual p.59–61 (`BELOW`), p.90 (`LOCATE`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261030-below-beam/`.
- Relevant local-only file hashes (sha256, first 8), `OVBBM0.TXT`: low-locate `05e8d6b9`, high-locate `578d361b`, mid-locate `7356502a`, low-nolocate `b13c62a5`, high-nolocate `b6e344d6`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle prepare below-beam` (`tools/OpenVirtue.Tools/BelowBeamProbeCases.cs`),
  run with `SDL_VIDEODRIVER=dummy`.
- below-view's bridge: the pad (x −12…−4, y 12…20) is a deck, floor 10 and ceiling 16, over an
  underpass, floor 0 and ceiling 8. The room, deck and underpass have `SKILL1` 1, 2 and 3.
- Each case beams the player to (−8, 16) with `PLAYER_SIZE` 4 and `PLAYER_Z` (the eye) 4, 9 or
  14, with or without `LOCATE`. It prints the position, `PLAYER_Z`, `PLAYER_HGT` and
  `HERE.SKILL1` for twelve cycles.

## Observations
| Case | Region | `PLAYER_Z`, `PLAYER_HGT` |
|---|---|---|
| eye 4, `LOCATE` | underpass (3) | 4, 0 throughout |
| eye 14, `LOCATE` | deck (2) | 14, 0 |
| eye 9, `LOCATE` | deck (2) | 9 at the beam; 10.35, −3.65 from the next cycle on |
| eye 4, no `LOCATE` | room (1) | 4, 0 |
| eye 14, no `LOCATE` | room (1) | 14, 10 (no fall in this probe, which has no movement script) |

## Invariant
- `LOCATE` puts the player in the stack's lowest storey whose ceiling is not below the eye, or
  the top storey when the eye is above them all. An eye between the underpass's ceiling (8) and
  the deck's floor (10) goes to the deck, where the eye clamp then lifts it to 0.35 above the floor
  (OV-20261019-eye-clamp).
- Without `LOCATE` a beam keeps the region (OV-20261019-proximity-events).

## Implementation
- `LevelTopology.StoreyHoldingEye` gives the storey.
- `WdlRuntime.Locate` uses it for the player.
- Tested by `BelowRegionTests.StoreyHoldingEye_PicksTheLowestStoreyWhoseCeilingIsNotBelowTheEye`.
- All five carriers replay line for line. The beam lines printed a frame before the first
  sampled cycle match since the replay takes the step into that cycle from the template's start-up
  stamp (`TCK=`, 8/32 of a tick before it here; `OracleReplay.StampStep`).

## Round 2: things and actors (2026-11-03)
Six more carriers (`round2/`) beam a thing or an actor from the room to (−8, 16), set its `HEIGHT`
and `LOCATE` it, then read its region back as the retail scripts do (`SET bb_r, obj.REGION;`, then
`bb_r.SKILL1`). Hashes (`OVBBM0.TXT`): thing-ground-4 `c2059df3`, thing-ground-12 `ca82c742`,
thing-rel-0 `4603357c`, actor-rel-0 `720133b0`, actor-ground-4 `b2e1d6f5`,
thing-ground-12-nolocate `39528551`.

| Case | Region after `LOCATE` |
|---|---|
| `GROUND` thing, `HEIGHT` 4 | underpass (3) |
| `GROUND` thing, `HEIGHT` 12 | deck (2) |
| `GROUND` actor, `HEIGHT` 4 | underpass (3) |
| thing, relative `HEIGHT` 0 | deck (2) |
| actor, relative `HEIGHT` 0 | deck (2) |
| `GROUND` thing, `HEIGHT` 12, no `LOCATE` | room (1) throughout |

- `LOCATE` places an object as the level start does (manual p.61): with `GROUND`, the storey whose
  ceiling is not below its feet; otherwise the top. The region never changes in the following
  cycles, and without `LOCATE` it stays the old one.
- `SET synonym, obj.REGION` takes the object's region; OpenVirtue read it as nothing before.

Implementation: `WdlRuntime.Locate` (the storey for a placed object), `WdlRuntime.RegionOf` and
`WdlInterpreter.ReferenceName` (`obj.REGION` as a reference). Tested by
`BelowRegionTests.Locate_ThingBeamedUnderABridge_TakesTheStoreyOfItsStart`. All six `OVBBM0`
traces match line for line. Their `OVPRB0` does not: OpenVirtue pushes the player off the objects
standing 8 steps away at the start (`PX` −0.930), where the original leaves it at (0, 0); that is
the start-up push-out (OV-20261029-overlap).

Addendum (2026-11-03): the push-out reaches no object 8 steps or more away
(OV-20261103-startup-clock: a 16-wide actor at (8, −2), `OVSUP0.TXT` `41046da6`, left the player at
(0, 0); the same actor 3 away pushed it out to 9.2). With that limit in
`LevelCollisionWorld.PushOutOfEntities`, the six round 2 `OVPRB0` traces (actor-ground-4 `d151ef6f`,
thing-ground-4 `832ebe7e`, …) match, and so do all six round 2 carriers.

## Open Questions
- An eye exactly at a storey's ceiling.

## Addendum: the round 2 screenshots and the sprites' order (2026-11-10)
Read from `round2/*/work/OVBBS0.PCX`: two things (magenta) and two actors (yellow), 16 wide, in a row 8
ahead at y 6, 2, −2 and −6. One of them is beamed away behind the eye.

- **Sprites are painted in order of their depth along the view, not of their distance from the eye, and
  do not hide one another by depth.** With the actor at y −2 and the things at y 6 and 2, the thing at
  y 6 shows over the actor, though it is 10 from the eye and the actor 8.25. OpenVirtue sorted by
  distance and let each sprite's own hiding depth settle overlaps: the actor covered the whole picture.
- At one depth a thing went over an actor in `actor-ground-4` and `thing-ground-*`, but under two actors
  in `thing-rel-0`. No single tie rule fits all four. Every sprite here stands at exactly one depth,
  which a level rarely has. OpenVirtue paints a thing after an actor of the same depth.

Implemented: `SpriteBuilder.OrderBackToFront(billboards, eye, forward)`, used by the host, whose sprite
batches no longer write depth. thing-ground-4, -12 and -12-nolocate now match to the pixel. Over the
seven round 2 screens the difference goes 1 114 677 → 472 723, `thing-rel-0` worse by 223 226. No other
screen of clip-view, below-view, sprite-shade or view-activity changes. Test:
`SpriteBuilderTests.OrderBackToFront_ByViewDepth_PaintsAThingAfterAnActorOfTheSameDepth`.
