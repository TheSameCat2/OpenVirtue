# OV-20261019-actor-contact: Contact distance, actors pushing the player, the vertical DIST test, and when DISTANCE starts
Date: 2026-10-19 (round 1), 2026-10-20 (rounds 2–7)
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-proximity-events, -clip-contact and -eye-clamp left these contact questions open:

1. How far from a solid thing or actor does the player stop, as a function of its width,
   `PLAYER_WIDTH` and `THING_WIDTH`/`ACTOR_WIDTH` (p.114)?
2. Does an actor walking into a standing player push it, and does its `IF_NEAR` fire in every
   pushing cycle? Does `CAREFULLY` (p.73) change that?
3. What happens to a player beamed inside a solid thing?
4. When does a thing's `MY.DISTANCE` leave its 65535.999 start value?
5. Is `DIST` compared with `<` or `<=`?
6. Why did some things never fire a `DIST` `IF_NEAR` (rounds 2–7)?

## Why It Matters
OpenVirtue's actors walked through the player, a beam could leave the player inside an object,
every object's `DISTANCE` was current from the first cycle, and `DIST` included its border.
Retail guards are `FOLLOW` actors, and many triggers are things with small `DIST`s.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.64–73 (thing/actor `DIST` and its hint,
  `IF_NEAR`, `PASSABLE`, `CAREFULLY`, `FOLLOW`), p.98–99 (synonyms), p.105 and p.113–114
  (`THING_DIST`, `ACTOR_DIST`, `THING_WIDTH`, `ACTOR_WIDTH`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-actor-contact/` (round 1 at the top,
  `round2/` … `round7/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`. Traces `OVACX0.TXT`:
  stop-width `aadbef1b…`, stop-factor `dba1b5de…`, actor-push `7ac82f97…`, actor-push-careful
  `3d9188f5…`, beam-inside `79e1f7b8…`, distance-refresh `a61b7372…`, dist-edge `128a54f2…`,
  beam-state `59285c26…`, refresh-20 `05c48d84…`, refresh-default `1f5406be…`, beam-order
  `d7506d32…`, dist-4wide `1130f1b8…`, dist-4wide-pw05 `e3596d02…`, dist-2wide `85c0bbde…`,
  dist-fine `2084bca8…`, dist-2wide-big `c2ee3e22…`, dist-inner `8305d973…`, dist-eye
  `fe1e19d2…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963, native Linux DOSBox-X 2026.10.01,
  `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. Every run went at 8 ticks per cycle.
- Fixture generator: `ovtool oracle prepare actor-contact --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/ActorContactProbeCases.cs`), on the room, sampler, walk and beam
  helpers of the proximity-events probe. Sprite textures are 2, 4 or 8 steps wide and as high.
  A `V` line comes from the global sampler in every cycle when a case asks for it.
- Cases:
  - `stop-width`: things 2, 4 and 8 wide with `IF_NEAR` (`DIST 0`); the player walks +X into each
    centre line with `PLAYER_WIDTH` 1.2, 2 and 0.5. `stop-factor`: a 4-wide thing and a 4-wide
    actor without a target, at the defaults, then `THING_WIDTH` 2 / `ACTOR_WIDTH` 0.5, then the
    reverse.
  - `actor-push`: a 4-wide `FOLLOW` actor, `SPEED 0.2`, `DIST 2`, `IF_NEAR`/`IF_FAR`, starts 30
    ahead of a player who stands still; after 200 ticks its `SPEED` is set to 0, then the player
    walks into it and backs off. `V` lines carry the actor's position and `DISTANCE`.
    `actor-push-careful`: the same actor with `CAREFULLY`.
  - `beam-inside`: 4-wide things, solid `DIST 0`, solid `DIST 6` and `PASSABLE`. The player is
    beamed 1 step from each centre (slightly off its line) and back to the origin, then walks
    through the passable one. `beam-state` (round 2) and `beam-order` (round 3): the `DIST 6`
    thing alone, beamed within `DIST`, inside it, within `DIST` again and walked into.
  - `distance-refresh`: `CLIP_DIST 10`, five 2-wide things (with or without `DIST 15`, with or
    without `LIBER`, within and beyond the border) read through synonyms on every `V` line while
    the player walks to x ≈ 20 and back. `refresh-20` and `refresh-default` (round 2): things at
    (40, ±6) and (−14, 20) with `CLIP_DIST 20` and with no `CLIP_DIST`.
  - `dist-edge`: a 2-wide thing with `DIST 10` and a passable wall with `DIST 10`; beams to
    |dx| + |dy| = 10, 9.99 and 10.01 from the thing and from the wall's nearer vertex.
  - `dist-4wide`, `dist-4wide-pw05`, `dist-2wide` (round 4), `dist-fine`, `dist-2wide-big`
    (round 5): five things of one size at x = 30, each with its own `DIST`, walked into along
    their centre lines at 0.1 steps per tick from 14 away, the eye at 8 (the carriers'
    `PLAYER_SIZE`/`PLAYER_Z`). `dist-inner` (round 6): beams to 4, 6 and 5.9 from a 2-wide
    `DIST 10` thing and to 4 from a 4-wide `DIST 6.5` one. `dist-eye` (round 7): a 2-wide `DIST 6.5`,
    a 2-wide `DIST 8` and a 4-wide `DIST 5` thing walked into with the eye at 3 and at 15.

## Observations
| Case | Trace | Reading |
|------|-------|---------|
| `stop-width` | stop distances from the centre: 2.22, 3.21, 5.20 (width 2, 4, 8) at `PLAYER_WIDTH` 1.2; 3.01, 4.01, 6.01 at 2; 1.53, 2.52, 4.51 at 0.5. The player slides round the object. `N` in every pushing cycle | contact is a circle of radius half the width plus `PLAYER_WIDTH`, with no wall clearance; `PLAYER_WIDTH` 0.5 is used as set |
| `stop-factor` | 3.21 for both at the defaults; 5.20 (thing) and 2.22 (actor) with 2 / 0.5; the reverse with 0.5 / 2 | `THING_WIDTH` and `ACTOR_WIDTH` scale the object's half width |
| `actor-push` | the actor closes at 1.59 a cycle. In the cycle it reaches 3.2, the player is moved to 3.212–3.221 ahead of it and `N` fires after the sampler; this repeats in every cycle while it walks, the player's y drifting with the line between the centres. Pressed against the back wall the actor overlaps the player (1.09 apart) and the player cannot move either way. `N` stops after `SPEED` 0 | an actor without `CAREFULLY` pushes the player ahead of it to the contact distance, and its `IF_NEAR` fires in every pushing cycle while the player is outside its `DIST` |
| `actor-push-careful` | the actor's step goes 1.6, 0.8, 0.8, 0.4, 0.2, 0.2, 0.1, 0.1, then 0; it rests at 3.334 from the player, who never moves. `N` in every cycle from the first shortened step | a `CAREFULLY` actor does not push. Its steps fit "the point two steps ahead must be at least the contact distance away, else halve the step, up to four times". It fires `IF_NEAR` whenever its full step is refused |
| `beam-inside` | beamed to (23, −9.5), 1.1 from the centre: in the next cycle, after the sampler, the player is at (21.126, −8.563), 3.21 from the centre on the line through the beam point, and `N0` fires once. The same for the `DIST 6` thing. Beamed into the passable thing: no move; the walk passes through it with no hook | a beam inside a solid object is resolved in the next movement: the player is pushed straight out to the contact distance, and the contact fires `IF_NEAR` |
| `beam-state`, `beam-order` | the `DIST 6` thing never fired a `DIST` `IF_NEAR` or `IF_FAR`: not on a beam to 4.5, not on a walk through 5.6, not on leaving; only the contact pushes fired. Its `DISTANCE` read 4.5, 34, 12 … correctly | explained by rule 6: the eye (8) is 6 above this 4-high thing's centre, not less than its `DIST 6` |
| `dist-4wide` | `DIST` 5 and 6: only contact `N`s at 3.69 and 3.27. 6.5: `N` 6.070, `F` 6.504. 7: `N` 6.863, `F` 7.305. 7.5: `N` 6.863, `F` 8.105 | a `DIST` of 6 or less is not tested for this 4-high thing with the eye at 8 |
| `dist-4wide-pw05` | the same pattern with `PLAYER_WIDTH` 0.5 (contact at 2.90) | the cut-off does not move with `PLAYER_WIDTH` |
| `dist-2wide` | `DIST` 3, 4, 4.5, 5 and 6: only contact `N`s at 2.90 | for this 2-high thing even 6 is not tested |
| `dist-fine` | 4-wide, `DIST` 6.1, 6.2, 6.3, 6.4, 6.45: every one `N` at 6.070, `F` at 6.504 | for 4-high things the cut-off lies between 6 and 6.1 |
| `dist-2wide-big` | 2-wide, `DIST` 6.25, 6.5, 7: contact only. 8: `N` 7.656, `F` 8.688. 10: `N` 9.242, `F` 10.290 | for 2-high things it lies between 7 and 8: it depends on the object after all |
| `dist-inner` | beams to 4, 6 and 5.9 from the `DIST 10` thing each fire `NA` with `MY.DISTANCE` 4.000, 6.000, 5.900, and the beams back `FA`; the beam to 4 from the `DIST 6.5` 4-wide thing fires `NB`, the beam back `FB` | no inner band: once `DIST` is above the cut-off, any distance below it counts |
| `dist-eye` | eye at 3: `NA` 6.070 (`DIST 6.5`, 2-wide), `NB` 7.656 (`DIST 8`), `NC` 4.484 (`DIST 5`, 4-wide), each with its `F`. Eye at 15: contact `N`s only, for all three | the cut-off is the eye's height above the object: with the eye at 8 it was 8 − 1 for 2-high things and 8 − 2 for 4-high ones, the height of the eye above the object's centre |
| `distance-refresh` | `CLIP_DIST 10`. The thing 8 away reads its distance from the first cycle. The others read 65535.999 until the player is within about 15 (13.69 and 14.43 the first values, 16.87 and 17.62 the last sentinel samples), with or without `DIST` or `LIBER`; after that they stay current at any range, out to 31.9 | a thing's `DISTANCE` starts at the player's first approach within 1.5 × the border |
| `refresh-20` | `CLIP_DIST 20`: the first values are 29.98 and 29.35; the thing 34 away never leaves 65535.999. The `DIST 25` thing's `N` comes at 19.57 | confirms 1.5 × `CLIP_DIST` (30); the `DIST` test waits for the border (20) |
| `refresh-default` | no `CLIP_DIST`: all three read their distance from the first cycle (46, 46, 34); `N` at 24.379, `F` at 25.086 for `DIST 25` | the default border is large |
| `dist-edge` | beamed to exactly 10 from the thing: nothing. To 9.990: `NT`. To 10.011: `FT`. The same three for the wall: nothing, `NW`, `FW` | `DIST` is strict: the border itself is outside |

## Conclusion
Confirmed for patched V3.963:

1. **Contact with a solid thing or actor is a circle** of radius (texture width × `THING_WIDTH`
   or `ACTOR_WIDTH`) / 2 + `PLAYER_WIDTH`. The player slides round it, with no extra clearance.
2. **An actor without `CAREFULLY` pushes the player ahead of it** to the contact distance, along
   the line between the centres, and fires its `IF_NEAR` in each pushing cycle while the player
   is outside its `DIST`. Walls stop the push.
3. **A `CAREFULLY` actor does not push**: it stops short of the player. It halves its step until
   the point two steps ahead is at least the contact distance away (at most four halvings, then
   no step), and fires `IF_NEAR` in every cycle its full step is refused.
4. **A beam inside a solid object is resolved at the next movement**: the player is pushed
   straight out from the centre to the contact distance, and the contact fires `IF_NEAR`.
   `PASSABLE` objects are not pushed out of.
5. **`DIST` is strict**: an object is near when |dx| + |dy| < `DIST`, for things, actors and walls.
6. **The `DIST` test of a thing or actor is also vertical**: the object is near only while
   |dx| + |dy| < `DIST` and the eye (`PLAYER_Z`) is less than `DIST` above or below the centre of
   its texture (its feet plus half its height). `MY.DISTANCE` stays the horizontal |dx| + |dy|.
   An object whose centre lies `DIST` or more below the eye fires only on contact. This explains
   the silent `DIST 4` `BERKELEY` objects of OV-20261019-clip-contact (eye 8, centre 1), so
   `BERKELEY` itself does not silence the hooks.
7. **A thing's or actor's `DISTANCE` reads 65535.999 until the player first comes within 1.5 ×
   its `CLIP_DIST` border**, `LIBER`, `DIST` or not. From then on it is current at any range.

## Confidence
high for rules 1, 2, 4, 5 and 7: each rests on several readings that agree, and rule 7 on three
`CLIP_DIST` settings. high for rule 6: the cut-off moved with the object height (two heights) and with the eye (three
heights), as predicted. Whether the reference is the texture centre or another point within the
lower half of the texture is not fully pinned down. medium for rule 3's halving
scheme: it fits all ten steps of one approach, at one speed.

## Round 8: when the contact's `IF_NEAR` runs (2026-10-30)
The beam-inside and beam-state traces were read again against OpenVirtue's replay. The player is
beamed at TICKS 24, and the samplers print the beam point in that cycle. `N0` then prints in the same
cycle (TICKS 24), with the player already pushed out to (21.126, −8.563) and `MY.DISTANCE` 34.000:
|24| + |−10|, the distance from where the player stood before the beam.
- **The contact `IF_NEAR` runs at once, inside the player's movement**, before any distance is
  refreshed. It does not wait for the next cycle's proximity test. `near-beam`
  (OV-20261019-proximity-events) shows that a `DIST` crossing does wait: its `N` comes the next cycle,
  with the fresh distance.
- OpenVirtue fired both in the next cycle. It now fires the contact events right after the player's
  movement (`WdlRuntime.DispatchContactEvents`). beam-inside, beam-state and dist-2wide now match in
  full, beam-order up to the push-out point's last unit, and 12 carriers match further, none less.

## Open Questions
- Whether walls have a vertical part too (wall-distance tested `DIST 6` walls successfully with the
  eye at 8), and whether `THING_WIDTH`/`ACTOR_WIDTH` or a `GROUND` height change the reference.
- An actor pressed into the player against a wall overlaps it, and the player then cannot move;
  a `CAREFULLY` actor set to `SPEED 0` nudged the player and the player's walk into it reversed.
  Neither is modelled.
- Whether `DISTANCE` stays current after the player leaves the 1.5 × border region for good, or
  only while the object stays "activated" (all runs came back within range).

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_DistBorderItselfIsOutside`,
  `Tick_ActorTargets_PlainActorPushesThePlayerAndFiresNearEveryPushingCycle`,
  `Tick_ActorTargets_CarefulActorStopsShortOfThePlayerAndFiresNearWhileBlocked`,
  `Apply_PlayerBeamedInsideASolidThing_IsPushedOutRadiallyAndFiresItsNear`,
  `Tick_Distance_ReadsTheSentinelUntilThePlayerFirstComesWithinOneAndAHalfBorders`,
  `Tick_PlayerProximity_ThingIsNearOnlyWhileTheEyeIsWithinDistOfItsCentre` (eye 3, 8, 15).
- `Tick_ObjectEachTick_BerkeleyThingRunsOnlyWithinDistAndAnEyeHighAboveItFiresNoHooks` replaces the
  clip-contact test that expected `BERKELEY` to silence the hooks.
- `Tick_PlayerProximity_WallCrossingFiresNearOutsideDistOnly` now enters `DIST` at 5, not at 6.

## Implementation Notes
`WdlRuntime.TryCurrentProximity` compares with `<` and, for things and actors, also requires
`VerticalOffsetFromCentre` (the eye against the feet plus half the texture height) below `DIST`. `MoveActor` pushes the player with `Player.PushAway`
(a move that ignores the pushing actor) for actors without `CAREFULLY`, and limits a `CAREFULLY`
actor's step with `CarefulStepScale`; both fire `IF_NEAR` through `FireActorContact` unless the
player is within `DIST`. `WdlPlayerMotion.Apply` calls `Player.PushOutOfObjects`
(`LevelCollisionWorld.PushOutOfEntities`) before moving the player; the object it touches fires
its `IF_NEAR` in the next cycle, like a walking contact. `RefreshDistances` keeps
`UnrefreshedDistance` until `DistanceStartFactor` × `ClipBorder`. The `BERKELEY` special case that
suppressed proximity hooks is gone (rule 6).
