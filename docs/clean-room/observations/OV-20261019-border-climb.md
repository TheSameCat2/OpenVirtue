# OV-20261019-border-climb: What stops the player from entering a neighbouring region
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-clip-contact saw the player walk from a floor-0 room onto a pad island with its floor
at 14, and at 16 (the ceiling height), with the default `PLAYER_CLIMB` (1.5, p.110). Every one of
those pads was bounded by `PASSABLE` walls. What stops the player from entering a neighbouring
region in the original runtime?

1. Does `PLAYER_CLIMB` limit the step through a wall that is not `PASSABLE`? Is the step measured
   from the current region's floor or from the player's feet (`PLAYER_Z − PLAYER_SIZE`)? Is a
   step equal to `PLAYER_CLIMB` allowed?
2. Does a low ceiling block, and is it compared with `PLAYER_SIZE` (headroom) or with the eye
   height `PLAYER_Z`?
3. Is stepping down always allowed, and can the player step back up?
4. Does `PASSABLE` skip the test?
5. Why does the room's outer wall (the room against a border region with floor = ceiling) hold?
6. Does pushing against a region border that blocks fire the wall's `IF_NEAR`?

## Why It Matters
OpenVirtue's `Player.Enterable` blocked a step of more than 1.5 from the current floor and a
region with less than 3 (later `PLAYER_SIZE`) steps of headroom, whatever the wall's flags.
Retail levels have stairs, ledges and low passages bounded by `PASSABLE` and plain walls, and
their movement scripts move `PLAYER_Z` themselves, so which walls the player can pass decides
where the player can go.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.53 (wall `PASSABLE`: "passable (permeable)
  to the player and actors"; `INVISIBLE`, `IMPASSABLE`), p.56–58 (region `FLOOR_HGT`, `CEIL_HGT`),
  p.109–110 (`PLAYER_SIZE`, `PLAYER_CLIMB`, `PLAYER_WIDTH`), p.111 (`PLAYER_HGT`, `FLOOR_HGT`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-border-climb/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`; rounds 2 and 3 under `round2/` and
  `round3/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261019-wall-distance). Traces `OVBCX0.TXT`: climb-1 `30c59e82…`, climb-2 `8c1baa37…`,
  climb-4 `e54bf343…`, climb-14 `28c677eb…`, ceil-4 `c43e6e16…`, ceil-0 `29f2bba7…`, step-down
  `641e3340…`, pass-4 `b16cd081…`, pad-solid `7ffafc4e…`, eye-10 `6ea07d95…`, ceil-4b
  `1df75b56…`, pass-10 `135df69a…`, edge-1 `8ea88298…`, ceil-4c `7e6192db…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. climb-1,
  climb-2 and climb-4 ran at about 4 cycles per tick, the other cases at about 8 ticks per cycle.
  No reading below depends on the rate.
- Fixture generator: `ovtool oracle prepare border-climb --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/BorderClimbProbeCases.cs`). The room of the proximity-events probe
  (floor 0, ceiling 16, closed by a border region with floor and ceiling at 16) with the pad
  island (x −12…−4, y 12…20). The pad's floor and ceiling are set per case, and its four
  boundary walls are plain (visible, no flag) unless the case says `PASSABLE`. They carry
  `IF_NEAR`/`IF_FAR` with `DIST 0`. The room has `SKILL1 1`, the pad `SKILL1 2`, and the pad
  prints `IF_ENTER`/`IF_LEAVE`.
- Each case runs several walks. A walk beams the player with `LOCATE` to (−8, 6), sets
  `PLAYER_SIZE`, `PLAYER_Z` and `PLAYER_CLIMB`, stands 8 ticks, walks +Y at 0.2 steps per tick for
  50 ticks (across the pad's south wall at y = 12, ending at y ≈ 16), stands, walks back and
  stands. Every trace line prints the position, `PLAYER_Z`, `FLOOR_HGT` and `HERE.SKILL1`. The
  step-down walks C–E start inside the pad and walk out. No movement script runs, so nothing
  but the runtime itself changes `PLAYER_Z`.

## Observations
All runs 2026-10-19 18:13–18:34 UTC: fourteen launches, each rc 0 with the completion marker and
no compiler error in `OVCON.TXT`. "Feet" is `PLAYER_Z − PLAYER_SIZE`; "step" is the pad floor
minus the feet.

| Case | Walk: pad floor/ceiling, `PLAYER_CLIMB`, `PLAYER_Z`, `PLAYER_SIZE` | Reading |
|------|------|------|
| climb-1 | 1/16; climb 0.5, 0.9 (Z 8, size 8, step 1) | blocked: the player stops at y 10.751–10.755 and slides slightly in x; `N` in every pushing cycle (95 and 96 in a row), `MY.DISTANCE` 4.85 → 4.52; no `F`, no `ENTER` |
| | climb 1.1, 1.5 (step 1) | `ENTER` at y 12.04 with `HERE.SKILL1` 2, one `N` per crossing, as in clip-contact |
| | climb 0.5, Z 9.5 (feet 1.5, step −0.5) | enters |
| climb-2 | 2/16; climb 1.5, 1.9 (step 2) | blocked, `N` every cycle |
| | climb 2.1 (step 2); climb 1.5, Z 9 (step 1); climb 0.5, Z 9.6 (step 0.4) | enter |
| climb-4 | 4/16; climb 1.5, 3.9 (step 4); climb 1.5, Z 10 (step 2) | blocked |
| | climb 4.1 (step 4); climb 1.5, Z 11 (step 1) | enter |
| climb-14 | 14/16; climb 1.5, 13.9, 14.1 (Z 8); climb 14.1, Z 1, size 1 (step 14) | all blocked |
| ceil-4 | 0/4; climb 1.5: Z 8 size 8; Z 8 size 3; Z 5 size 5 | blocked |
| | Z 3, size 3 | enters |
| ceil-0 | 0/0; climb 1.5 and 20 (Z 8); Z 0.5, size 0.5 | all blocked |
| step-down | −4/16; climb 1.5 and 0.5, Z 8 (step −4) | enter; `PLAYER_Z` stays 8 in the pad (feet 4 above its floor), and both walk back out into the room |
| | from inside the pad: climb 1.5, Z 4 (feet on the pad floor, step 4) | blocked at y 13.25 by the south wall, and on the way back at y 18.75 by the north wall (also a room border): `N` while pushing |
| | climb 4.5, Z 4; climb 0.5, Z 8 (step 0) | leave the pad |
| pass-4 | 4/16, `PASSABLE`; climb 0.5 and 1.5, Z 8 | enter |
| pad-solid | 16/16; climb 1.5 and 20 (Z 8); climb 20, Z 0.5, size 0.5 | all blocked |
| | the room's back wall from the origin, climb 20 | stops at x −14.748 |
| eye-10 | 10/16; climb 7.5, Z 11, size 8 (step 7, headroom 6 < size 8) | enters |
| | climb 1.5, Z 10.5, size 1 | enters |
| | climb 1.5, Z 9.9, size 1 (step 0.9, eye 0.1 below the floor); climb 7, Z 9.9, size 4 | blocked |
| | climb 2.5, Z 15.9 and 16.1, size 8 | `PLAYER_Z` reads 15.650 in the room before the walk; both enter |
| ceil-4b | 0/4; climb 1.5, Z 3.9, size 3 | enters; `PLAYER_Z` 3.899 → 3.650 at `ENTER` |
| | Z 4.1, size 3 | blocked |
| | Z 3.5, size 5 (step 1.5 = climb) | blocked |
| pass-10 | 10/16, `PASSABLE`; Z 8, size 8 and size 3 | enter; `PLAYER_Z` 8 → 10.350 at `ENTER`, and stays there back in the room |
| | Z 12 | enters; `PLAYER_Z` stays 12 |
| edge-1 | 1/16; climb 1, Z 8, size 8 (step 1 = climb) | blocked |
| | climb 1.5, size 1, Z 1.1, 1.3, 1.4 (eye 0.1–0.4 above the floor) | enter; Z 1.1 and 1.3 read 1.350 from `ENTER` on |
| ceil-4c | 0/4; climb 1.5, Z 3.6, size 5 (step 1.4); climb 1.6, Z 3.5, size 5; climb 1.5, Z 3.5, size 3 | enter |

## Conclusion
Confirmed for patched V3.963, for the player walking through a wall between two regions:

1. **A `PASSABLE` wall lets the player through whatever the heights** (pass-4, pass-10, and
   clip-contact's border-high and border-ceil).
2. **Through a wall that is not `PASSABLE`, the player enters the other region only if all three
   hold:**
   - its floor is **less than** `PLAYER_CLIMB` above the player's feet, `PLAYER_Z − PLAYER_SIZE`.
     A step equal to `PLAYER_CLIMB` blocks. The step is taken from the feet, not from the current
     floor: a raised `PLAYER_Z` climbs higher, and a player who stepped down without sinking walks
     straight back up.
   - the eye `PLAYER_Z` is above its floor (0.1 above was enough).
   - the eye is below its ceiling (3.9 under 4 entered, 4.1 did not).

   `PLAYER_SIZE` and the region's headroom count only through these. A player taller than the
   headroom enters as long as the eye fits.
3. **A region with floor = ceiling holds the player** because no eye can lie above its floor and
   below its ceiling. That is why the room's outer walls hold even with `PLAYER_CLIMB` 20.
4. **Stepping down is always allowed by the step rule.** The runtime does not lower `PLAYER_Z`
   by itself.
5. **Pushing against a region border that blocks fires its `IF_NEAR` in every pushing cycle,**
   with no `IF_FAR`, as for a solid wall (OV-20261019-wall-pass-clip).
6. **The runtime keeps the eye within the player's region:** `PLAYER_Z` is raised to the floor
   + 0.35 when it is lower (pass-10, edge-1). It is lowered to the ceiling − 0.35 when it is higher
   (eye-10, ceil-4b, and clip-contact's border-ceil, where floor and ceiling were both 16 and the
   eye read 15.650). Otherwise it is left alone, so the feet may hang above or below the floor.

## Confidence
high for rules 1–5: fourteen cases, every walk fits them, and the boundaries were bracketed on
both sides (step 1 vs `PLAYER_CLIMB` 0.9/1/1.1, 2 vs 1.9/2.1, 4 vs 3.9/4.1; eye 9.9 vs 10.5 over
floor 10; eye 3.9 vs 4.1 under ceiling 4). medium for rule 6: the margin 0.35 was read on four
cases, but the order of the two clamps is only known from the floor-16/ceiling-16 pad.

## Open Questions
Answered by OV-20261019-eye-clamp:
- The player stopped 1.25 from a blocking region border (y 10.751–10.755 against y = 12), not at
  `PLAYER_WIDTH` 1.2. Solid walls and blocking borders both stop the player 0.01–0.05 beyond
  `PLAYER_WIDTH`, whatever its value; OpenVirtue now stops at `PLAYER_WIDTH` + 0.05.
- The eye clamp (rule 6) is implemented as `Player.ClampEye`, after every movement frame.
  `PLAYER_VZ` moves the eye freely (the feet sink below the floor), the clamp leaves `PLAYER_VZ`
  alone and adds its correction to `IMPACT_VZ`.

Still open:
- How exactly the eye has to lie above the floor (strictly, or by less than 0.1), and whether
  the floor is sampled at the player's position on slopes, was not probed.
- `IMPASSABLE`, an `INVISIBLE` region border (p.53 says it must have one region on both sides),
  actors (`ACTOR_CLIMB`, p.113) and things were not probed. Answered for all but things by
  OV-20261019-border-kinds, which also measured the eye strictly above the floor.

## Tests To Add Or Update
- `PlayerTests.MoveHorizontal_IntoRegionBorder_EntersWhenTheFloorIsLessThanClimbAboveTheFeet`.
- `PlayerTests.MoveHorizontal_IntoRegionBorder_EntersOnlyWhenTheEyeLiesBetweenTheNewFloorAndCeiling`.
- `PlayerTests.MoveHorizontal_PassableRegionBorder_IgnoresStepAndCeiling`.
- `PlayerTests.MoveHorizontal_AfterSteppingDownWithoutSinking_WalksBackUpButNotFromThePadFloor`.
- `PlayerTests.TakeTouchedObjects_PushingAgainstAnUnenterableRegionBorder_ReportsItOnEveryMove`.
- `PlayerTests.MoveHorizontal_DownTallLedge_CrossesPortalWithoutReverseCollision` and
  `MoveHorizontal_UpTallLedge_RemainsBlocked`: the player now starts on its own region's side of
  the ledge (the first region of a WMP wall is left of v1→v2, y-up). The old start stood on the
  other side, and the test passed only because the old rule measured the step from the floor.

## Implementation Notes
`Player.CanCross` lets the player through any `PASSABLE` wall. A wall between two regions that is
not `PASSABLE` is crossable when `Player.Enterable` holds:
`floor − (eye − EyeHeight) < StepUp`, `eye > floor` and `eye < ceiling`. Here the eye is
`Position.Y` (`PLAYER_Z`), `EyeHeight` is `PLAYER_SIZE` and `StepUp` is `PLAYER_CLIMB`, and the
floor and ceiling are the other region's, sampled at the player's position. `MinHeadroom` is
gone. A blocked border is a solid wall to `LevelCollisionWorld`, which already reports it as
touched on every pushing move, so `WdlRuntime` fires its `IF_NEAR` every cycle. Rule 6 and the
stop distance are implemented with OV-20261019-eye-clamp.
