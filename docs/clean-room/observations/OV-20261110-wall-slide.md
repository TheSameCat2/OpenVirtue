# OV-20261110-wall-slide: The player sliding along a diagonal wall
Date: 2026-11-10
Status: confirmed for the middle of a wall, implemented; wall ends, a deep first contact and the 3:4 wall open
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
  2. Measure d = ⌊(q − a)·N / 1024⌋. Here a is the wall's first vertex and N is the wall's normal toward
     the player, in 1/1024, each component rounded.
  3. Push back along N to h = `PLAYER_WIDTH` + 51/1024: q += ⌊(h − d)·N / 1024⌋, each axis cut down.
- The 3:4 wall (y = 3x/4 − 9, step 0.3) takes the step's part along the wall cut down, (198, 148). That
  creeps 0.4/1024 toward the wall per cycle, and a (197, 149) cycle restores it whenever the distance
  would fall under about 1278.5. The rule above fits it only with N = (−615, 820) and h = 1280, where the
  normal rounds to (−614, 819). How the original makes this wall's normal is open.
- A first contact that comes in deep differs by a unit: `w45-s02-w14` from 1334.9 (the original
  (10502, 313), the rule (10501, 314)) and w63-s02 from 1053.8.
- Past a wall's end the player rounds the end point. The rule does not cover that.

## Rules
- **A single wall's hold is the whole step pushed back in 22.10**, as above. Along an axis it gives the
  earlier hold at `PLAYER_WIDTH` + 51/1024 exactly (OV-20261019-eye-clamp).

## Confidence
High for the middle of a wall of slope 1/2, 1 and 2. The 3:4 wall shows the normal's rounding is not
simply to the nearest unit.

## Open Questions
- The 3:4 wall's normal and hold.
- A deep first contact.
- The wall's ends.
- Two or more walls at once (corners): still OpenVirtue's float solver, rounded to the nearest unit.

## Implementation
`Player.HoldOffWall`, used by `Player.MoveHorizontal` when exactly one wall stops the step within its
span. Test: `PlayerTests.MoveHorizontal_AlongDiagonalWall_SlidesAsTheOriginalIn1024ths`. Probe cases:
`tools/OpenVirtue.Tools/WallSlideProbeCases.cs`.

With the rule, the probe's carriers match to lines 191 (45°), 203 (1:2), 205 (2:1) and 109 (45°, 0.5),
up from 108, 146, 112 and 76. Apathy's walk matches 399 lines (396 before). The replay summary keeps
its 1059 carriers.
