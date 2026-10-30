# OV-20261019-eye-clamp: How PLAYER_VZ moves the eye, the eye clamp, and the wall stop distance
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-border-climb saw the runtime keep `PLAYER_Z` (the eye) within the floor + 0.35 and
the ceiling − 0.35 of the player's region with no movement script running. It also saw the
player stop about 1.25 from a blocking region border with the default `PLAYER_WIDTH` 1.2. Both
were left unimplemented. OpenVirtue stopped `PLAYER_VZ` motion with the feet on the floor
(eye = floor + `PLAYER_SIZE`) and at the ceiling, set `PLAYER_VZ` to the speed it actually
moved, and kept `PLAYER_WIDTH` from solid walls.

1. Where does `PLAYER_VZ` (p.111) stop the eye, going down and going up? Does it stop with the
   feet on the floor or at the clamp?
2. Is `PLAYER_VZ` changed when the eye stops, and what happens to `IMPACT_VZ`?
3. How far does `PLAYER_VZ` move the eye per frame, and how does that depend on `TIME_CORR`?
4. Does the 0.35 margin depend on `PLAYER_WIDTH` or `PLAYER_SIZE`?
5. How far from a solid wall and from a blocking region border does the player stop, for
   different `PLAYER_WIDTH`s and walking speeds? Is `PLAYER_WIDTH` held at the manual's minimum
   of 1.2 (p.109)?

## Why It Matters
The retail movement scripts drive gravity and jumps through `PLAYER_VZ`. They read `PLAYER_HGT`
(the feet above the floor, p.115) and push back up with a force proportional to it when it is
below 0. That only works if the runtime lets the feet sink below the floor. How far `PLAYER_VZ`
moves the eye per frame sets the speed of every jump and fall, and the stop distance decides
which gaps and doorways the player fits through.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.109 (`PLAYER_WIDTH`: "Default and minimum
  value 1.2 steps", `PLAYER_SIZE`), p.111 (`PLAYER_VZ`), p.115 (`PLAYER_HGT`), p.116
  (`IMPACT_VZ`), p.117 (`TIME_CORR`). The retail `move.wdl` was read for the facts above (how
  it uses `PLAYER_VZ` and `PLAYER_HGT`); nothing was copied.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-eye-clamp/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`; the slow-CPU rerun under `round2/`,
  the rate series under `round3/`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261019-wall-distance). Traces `OVECX0.TXT`: vz-move `57c5bd9a…`, stop-solid `648dc091…`,
  stop-border `afdac5e6…`, vz-slow `6a92a0ac…`, vz-rate (default, `cycles=fixed 3000` and 6000,
  byte-identical) `78e6108f…`, vz-rate-c12000 `e774db70…`, vz-rate-c25000 `98dee736…`,
  vz-rate-c50000 `8d240430…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. To vary the
  frame rate, copies of the carrier ran with `[cpu] core=normal` and `cycles=fixed` 1500, 3000,
  6000, 12000, 25000 and 50000 added to `dosbox-x.conf` by hand. The `TIME_CORR` each run
  printed is the reading that counts, not the cycle setting.
- Fixture generator: `ovtool oracle prepare eye-clamp --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/EyeClampProbeCases.cs`). The room of the proximity-events probe
  (floor 0, ceiling 16). While `ec_p` is set, the global `EACH_TICK.1` sampler prints a `V`
  line in every cycle with `TIME_CORR`, the position, `PLAYER_Z`, `PLAYER_VZ`, `PLAYER_HGT`,
  `IMPACT_VZ` and `PLAYER_WIDTH`. It runs before the runtime moves the player, so the change
  between two `V` lines is one frame's movement.
- Cases:
  - `vz-move`: from `PLAYER_Z` 8, `PLAYER_SIZE` 3: A sets `PLAYER_VZ` −0.25 once, B +0.25 once,
    C and D write ∓0.25 in every cycle, E has `PLAYER_SIZE` 8, F `PLAYER_WIDTH` 3. Each moves for
    48 ticks. G sets `PLAYER_Z` 0.1 and H 15.95 with `PLAYER_VZ` 0 and `PLAYER_WIDTH` 3.
  - `vz-rate`: from `PLAYER_Z` 8, `PLAYER_VZ` 0.05, −0.1, 0.5, −1 and 0.02 for 16 ticks each.
    `round2/vz-slow` is `vz-move` at `cycles=fixed 1500`.
  - `stop-solid`: a free-standing wall from (24, −8) to (24, 8), with the room on both sides.
    Pushes along +X from (18, 0) with `PLAYER_WIDTH` 1.2, 2, 3 and 0.5 at 0.2 steps per tick,
    1.2 at 0.05 and at 1 step per tick, and 3 at 0.05.
  - `stop-border`: the pad (x −12…−4, y 12…20) with floor and ceiling at 16 behind plain walls.
    Pushes along +Y from (−8, 6) into its south wall (y = 12) with `PLAYER_WIDTH` 1.2, 2 and 3,
    and 1.2 at 0.05 steps per tick.

## Observations
All runs 2026-10-19 18:57–19:08 UTC: ten launches, each rc 0 with the completion marker and no
compiler error in `OVCON.TXT`. `vz-move`, `stop-solid` and `stop-border` ran at about 4 cycles
per tick (`TIME_CORR` alternating 0.250 and 0.281). Every `vz-rate` run with `cycles=fixed` 6000
or less, and also the one without a cycle setting, ran at `TIME_CORR` 8.000 in every cycle.
`PLAYER_WIDTH` 1.2 prints as 1.199.

| Case | Reading |
|------|---------|
| vz-move A, C | the eye falls 1.000 per cycle (8, 7, …, 3 with the feet on the floor, 2, 1), then reads 0.350 with `PLAYER_HGT` −2.650 and stays there. `PLAYER_VZ` stays −0.250 throughout; the set-once and the every-cycle writes behave the same. `IMPACT_VZ` gains 0.350 on the stopping cycle, then 1.000 in every further cycle (172.35 after 185 lines) |
| vz-move B, D | rises 1.000 per cycle to 15.650 under the ceiling of 16. `PLAYER_VZ` stays 0.250. `IMPACT_VZ` loses 0.350, then 1.000 per cycle |
| vz-move E | `PLAYER_SIZE` 8: the same fall to 0.350, `PLAYER_HGT` −7.650 |
| vz-move F, G, H | `PLAYER_WIDTH` 3: F stops at 0.350. G (`PLAYER_Z` 0.1) reads 0.350 the next cycle, `IMPACT_VZ` +0.250. H (15.949) reads 15.650, `IMPACT_VZ` −0.299 |
| vz-slow | `TIME_CORR` 8.000: 2.000 per cycle for `PLAYER_VZ` ±0.25; the same stops at 0.350 and 15.650 |
| vz-rate at `TIME_CORR` 8 | 0.398, −0.797, 4.000, −7.650 (to the clamp) and 0.156 per cycle for 0.05, −0.1, 0.5, −1 and 0.02: `PLAYER_VZ` × 8 |
| vz-rate-c12000 | `TIME_CORR` 4.19–4.72: `PLAYER_VZ` × `TIME_CORR` in every cycle (0.210 at 4.219, 0.233 at 4.688 for 0.05; 2.109 and 2.359 for 0.5) |
| vz-rate-c25000 | `TIME_CORR` 2.09–2.38: `PLAYER_VZ` × 4 (0.199–0.200, −0.398, 2.000, −4.000, 0.078), whatever `TIME_CORR` |
| vz-rate-c50000 | `TIME_CORR` 1.03–1.31: `PLAYER_VZ` × 4 again |
| stop-solid | the push stops at x 22.755 (1.245 from the wall) with width 1.2, 21.954 (2.046) with 2, 20.950 (3.050) with 3 and 23.450 (0.550) with 0.5, which `PLAYER_WIDTH` reads back as 0.500. At 0.05 steps per tick 1.249 (width 1.2) and 3.036 (width 3); at 1 step per tick 1.249 |
| stop-border | y 10.755 (1.245 from y = 12), 9.954 (2.046) and 8.950 (3.050) for widths 1.2, 2 and 3. At 0.05 steps per tick 10.790 (1.210), after sliding 2.7 steps along the wall |

## Conclusion
Confirmed for patched V3.963:

1. **`PLAYER_VZ` moves the eye freely.** The feet sink below the floor (`PLAYER_HGT` −2.65 with
   `PLAYER_SIZE` 3, −7.65 with 8). Only the eye clamp stops the eye.
2. **After the player moves, the runtime clamps the eye to [floor + 0.35, ceiling − 0.35] of
   the player's region.** The ceiling wins when the two overlap (OV-20261019-border-climb). The
   margin does not depend on `PLAYER_WIDTH` or `PLAYER_SIZE`. The clamp also catches a `PLAYER_Z`
   set out of range by a script.
3. **The clamp does not change `PLAYER_VZ`.** It adds its correction, in steps, to `IMPACT_VZ`:
   +0.35 when the eye first stops 0.35 above the floor, then +|Δz| in every frame that
   `PLAYER_VZ` keeps pushing. A scripted `PLAYER_Z` that the clamp corrects counts too. Nothing
   resets `IMPACT_VZ`.
4. **One frame moves the eye by `PLAYER_VZ` × max(`TIME_CORR`, 4).** Above `TIME_CORR` 4 the move
   is `PLAYER_VZ` × `TIME_CORR`, as for the horizontal speeds. At every `TIME_CORR` from 0.25 to
   2.4 it was 4 × `PLAYER_VZ` per frame, independent of the frame's `TIME_CORR`. Moves are
   quantized to about 1/256 step (0.398 for 0.4).
5. **A solid wall and a blocking region border stop the player 0.01–0.05 beyond `PLAYER_WIDTH`**,
   mostly 0.045–0.05, at any speed. **`PLAYER_WIDTH` below 1.2 is not raised to 1.2:** 0.5 held the
   player 0.55 from the wall.

## Replay (2026-10-30)
`round2/vz-slow` and the `round3/vz-rate-c*` speed variants were copied by hand from `vz-rate`
without a `case.json`, so the oracle-diff harness did not know their trace file and replayed them on a
fixed clock. With a local `case.json` naming `OVECX0.TXT`, `vz-slow` (120 lines), `vz-rate-c3000` and
`vz-rate-c6000` (49 lines each) match in full. `c50000` matches 81 of 106 lines, and `c12000`/`c25000`
differ from the start-up line (`GO`) before their first sample.

## Confidence
high for 1–3 and 5: every move of ten runs fits them. medium for the bend of rule 4 at
`TIME_CORR` 4: the points either side are 2.4 and 4.2. `TIME_CORR` 2.4–4.2 was not reached, and
neither was a reading of `TIME_CORR` exactly 4.

## Open Questions
- Why the runs without a cycle setting ran at `TIME_CORR` 0.25 in one launch and 8 in another
  is not known. The readings depend only on the `TIME_CORR` printed.
- What sets the 0.01–0.05 spread of the stop distance (and the 1.210 of a slow push that slid
  along the border) is not measured. OpenVirtue stops at `PLAYER_WIDTH` + 0.05.
- Whether actors, things, slopes (the floor sampled at the player's position) and regions with a
  `BELOW` link (`IF_DIVE`/`IF_ARISE`) clamp the same way is not probed.
- Whether contact with things and actors keeps the same 0.05 beyond `PLAYER_WIDTH` + half their
  width is not probed (OV-20261015-flic-scheduler-detail measured about 2.2 for a 2-step-wide
  object). Answered by OV-20261019-actor-contact: no, contact is half the width plus
  `PLAYER_WIDTH`, with no clearance.

## Tests To Add Or Update
- `PlayerTests.ClampEye_KeepsTheEyeAMarginInsideTheRegion` (border-climb's pass-10, edge-1,
  eye-10, ceil-4b and border-ceil readings).
- `PlayerTests.MoveVertical_PastTheFloor_LeavesTheEyeToTheClamp`.
- `PlayerTests.MoveHorizontal_IntoSolidWall_StopsPlayerWidthPlusClearanceAway` and
  `MoveHorizontal_IntoSolidWall_StopDistanceFollowsPlayerWidth`. The other wall-stop tests expect
  `Width + WallClearance`.
- `WdlPlayerMotionTests.Apply_VerticalVelocity_MovesByPlayerVzTimesTimeCorrectionButAtLeastFourTicks`,
  `Apply_DownwardVelocity_SinksTheFeetAndStopsTheEyeAtTheFloorClamp`,
  `Apply_UpwardVelocity_StopsTheEyeAtTheCeilingClamp` and
  `Apply_ScriptedEyeOutsideTheRegion_IsClampedAndCountedInImpact`.
  `Apply_LivePlayerDimensions_UpdateCollisionConfiguration` keeps `PLAYER_WIDTH` 0.5.

## Implementation Notes
`WdlPlayerMotion.Apply` moves the eye by `PLAYER_VZ × max(TIME_CORR, 4)` with
`Player.MoveVertical`, which only follows linked `BELOW` surfaces. It then calls
`Player.ClampEye` (margin `Player.EyeMargin` = 0.35, ceiling last) and adds the correction to
`IMPACT_VZ`. It leaves `PLAYER_VZ` as the script set it. `Player.Grounded` holds while the feet
are on or below a floor the player cannot dive through. `LevelCollisionWorld.Move` takes a
`wallClearance` that solid walls add to the radius, and `Player` passes
`Player.WallClearance` (0.05). `PLAYER_WIDTH` is used as set, down to 0.
