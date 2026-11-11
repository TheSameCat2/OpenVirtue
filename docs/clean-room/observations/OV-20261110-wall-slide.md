# OV-20261110-wall-slide: The player sliding along a diagonal wall
Date: 2026-11-10
Status: confirmed for the middle of a wall and its ends, implemented; one deep first contact open
Observer: Claude Code (Opus 5.5)
Implementer: same

## Question
Apathy's walk slid along a diagonal wall: the original held the player 1484 to 1486/1024 from it, and
OpenVirtue held it a unit nearer and slid a unit short in one axis (OV-20261110-retail-play round 4).
How does the original hold a step against a wall that is not along an axis, to the 1/1024?

## Why It Matters
A player slides along walls all the time. A unit lost or gained per cycle moves the player, then
`STEPS`, `WALK` and the eye that follow it.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Reference material consulted: none beyond the earlier wall notes (OV-20261019-eye-clamp).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261110-wall-slide/`, six synthetic carriers made by
  `ovtool oracle prepare wall-slide`.
- Relevant local-only file hashes (sha256, first 8), `OVWSL0.TXT`: w27-s02 `9b4aa7b1`, w37-s03 `e7d2c025`,
  w45-s02 `82d46d66`, w45-s02-w14 `c4112a91`, w45-s05 `15b62abe`, w63-s02 `e3ea6a1b`.
- Confirmation that no proprietary code/assets are committed: the carriers are generated.

## Method
Each carrier puts one free-standing wall, the room on both sides and whole-step vertices, across the
player's path from the origin: y = x − 12 from (4, −8) to (20, 8), y = x/2 − 10, y = 2x − 24 and
y = 3x/4 − 9. A global `EACH_TICK` sampler sets `PLAYER_VX` to the step over `TIME_CORR` every cycle
(0.2, 0.3 or 0.5, so 204, 307 or 512 raw a cycle; `PLAYER_VY` 0, which the minimum step makes 4) and
prints the position × 1024. `w45-s02-w14` sets `PLAYER_WIDTH` 1.4.

## Readings
- Before the wall the player steps (204, 4) a cycle. A step ending inside the hold but outside
  `PLAYER_WIDTH` is not stopped: w27-s02 ends one at 1270 from the wall line.
- Along the middle of the 1:2 wall the player moves (164, 82) every cycle. That is the step's part along
  the wall, (164.8, 82.4), cut down. Along the 45° wall it moves (103, 104) and (104, 103) in turn,
  1279.15 and 1279.86 from the line. OpenVirtue moved (104, 104) and held the player at 1278.4, inside
  `PLAYER_WIDTH` + 51/1024.
- One rule gives every middle-of-wall cycle of the 45°, 1:2 and 2:1 walls, at both widths and both steps
  (585 of 587 cycles; the two others are first contacts):
  1. Take the whole step, q = p + s.
  2. Measure d = ⌊(q − a)·n⌋. Here a is the wall's first vertex and n is the wall's unit normal toward
     the player, not rounded.
  3. Push back along n to h = `PLAYER_WIDTH` + 51/1024: q += ⌊(h − d)·n⌋, each axis cut down.
- The 3:4 wall (y = 3x/4 − 9, step 0.3) settles the normal: it is the exact unit normal. A normal rounded to
  1/1024, (−614, 819), misses 63 of its 96 cycles; the exact one misses none. The exact normal also fits
  the width-1.4 first contact the rounded one missed. Over all six carriers, one middle-of-wall cycle
  misses: w63-s02's first contact, from 1053.8.
- **A wall's ends hold the step first, each like a thing of no width; the line then holds it only if it
  is still within `PLAYER_WIDTH` of it.** Rounding the 45° wall's end, the player stands 1268.7 to
  1269.5/1024 from the end point (1467.5 to 1468.5 at width 1.4). That is the push-out from a thing, to
  (c − 1)²/1024 + 102 with c = `PLAYER_WIDTH` (OV-20261103-startup-clock). The step-into-a-thing
  push-back, c²/1024 + 98, leaves cycle 188 a unit off. On cycle 188 the step's end came
  within 1185 of the end point, which put it at (19369, 8803). That is 1217.6 from the line, which then
  held it at (19325, 8846), as printed. On cycle 189 the end's push left the step 1248 from the line, and
  the line let it be. After that the positions are within 1/1024 of the original's: the point push's
  rounding is no better known for walls than for things.

## Rules
- **A single wall's hold is the whole step pushed back in 22.10**, as above. Along an axis it gives the
  earlier hold at `PLAYER_WIDTH` + 51/1024 exactly (OV-20261019-eye-clamp).
- **Its ends come first**, as things of no width that push the player out, and the line holds only a step still within
  `PLAYER_WIDTH` of it.

## Confidence
High for the middle of a wall of slope 1/2, 1 and 2. The 3:4 wall shows the normal's rounding is not
simply to the nearest unit.

## Open Questions
- w63-s02's first contact, from 1053.8: a unit off.
- Retail walls with corner points that are not whole steps (Apathy's (−1.447, 42.397), Heart's
  (−8.481, 181.039), Legalism's (−1195.154, 944.359)). Truncating the corners to 1/1024 instead of
  rounding them keeps Apathy's walk right 4 cycles longer and Heart's 6, but Legalism's 3 shorter. On
  Legalism's walk at line 370 the original stops a step that OpenVirtue's rounded corners put 1434.24
  from the line, against the 1.4 width's 1433. Truncated corners give 1433.88. The width is 1433,
  not 1434: 1434 loses Apathy's climb border at line 242. OpenVirtue's stop is decided by its float
  solver, not in 22.10, so neither reading is tried yet.
- The point push's last unit at a wall's end.
- Two or more walls at once (corners): still OpenVirtue's float solver, rounded to the nearest unit.

## Implementation
`Player.HoldOffWall`, used by `Player.MoveHorizontal` when exactly one wall stops the step, and
`LevelCollisionWorld.StepOffPoint` for its ends. Test: `PlayerTests.MoveHorizontal_AlongDiagonalWall_SlidesAsTheOriginalIn1024ths`. Probe cases:
`tools/OpenVirtue.Tools/WallSlideProbeCases.cs`.

With the rule (exact normal since 2026-11-10's second batch), the probe's carriers match to lines 192 (45°),
203 (1:2), 205 (2:1), 109 (45°, 0.5), 156 (3:4) and 191 (45°, width 1.4), up from 108, 146, 112, 76, 89
and 107. Apathy's walk matches 400 lines (396 before). Heart's walk meets the same kind of 3:4 wall,
with vertices that are not whole steps. The replay summary keeps all its carriers.
