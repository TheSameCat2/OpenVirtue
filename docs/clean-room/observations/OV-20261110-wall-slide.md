# OV-20261110-wall-slide: The player sliding along a diagonal wall
Date: 2026-11-10
Status: confirmed for the middle of a wall, long walls, corners and its ends, implemented; Legalism's unit open
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
- Legalism's walk parts at line 367, at the first stop on a long maze wall: there the original's push
  is along y alone, (253, −98) on a step of (253, 4), which points to another wall or an end.

## Round 2 (2026-11-10): corners off the 1/1024 grid
`round2/f45-s02`, `f45-s02-w14` and `f37-s03`: the 45° and 3:4 walls with corners such as (4.154, −7.9),
4253.696 and −8089.6 units, so that rounding, cutting toward zero and flooring give different units.
Hashes of `OVWSL0.TXT` are in the carriers' folders.

| Corners | Cycles off, of 129, 96 and 128 in the middle of the wall |
|---|---|
| rounded | 76, 10, 77 |
| floored | 1, 27, 0 |
| exact decimals | 1, 10, 39 |
| **cut toward zero** | **0, 2, 0** |

- **A map vertex's x and y are cut toward zero to 1/1024**, as the player start's are
  (OV-20261110-retail-play). Implemented when the map loads (`LevelLoader`). With it the three carriers match
  to lines 192, 124 and 193. Apathy's walk holds to line 403 (400 before) and Heart's to 295 (289), but
  Legalism's to 367 (370).

## Implementation
`Player.HoldOffWall`, used by `Player.MoveHorizontal` when exactly one wall stops the step, and
`LevelCollisionWorld.StepOffPoint` for its ends. Test: `PlayerTests.MoveHorizontal_AlongDiagonalWall_SlidesAsTheOriginalIn1024ths`. Probe cases:
`tools/OpenVirtue.Tools/WallSlideProbeCases.cs`.

With the rule (exact normal since 2026-11-10's second batch), the probe's carriers match to lines 192 (45°),
203 (1:2), 205 (2:1), 109 (45°, 0.5), 156 (3:4) and 191 (45°, width 1.4), up from 108, 146, 112, 76, 89
and 107. Apathy's walk matches 400 lines (396 before). Heart's walk meets the same kind of 3:4 wall,
with vertices that are not whole steps. The replay summary keeps all its carriers.

## Round 3 (2026-11-11): long walls, corners and the ends' push
Legalism's walk stopped against a 70-step maze wall 1/1024 nearer than the exact normal holds it, and
Heart's walked into the corner of two walls (OV-20261110-retail-play round 6). Three carriers, all with
`PLAYER_WIDTH` 1.4, walk the player at 0.2 a cycle into the line y = x/5 − 2:
- `round3/long-a` has a 61-step wall from (−15, −5) to (45, 7). The components of its length overflow a
  32-bit square.
- `round3/long-b` is the same wall with its vertices swapped.
- `round3/short` has 20 steps of the same line, from (5, −1) to (25, 3), which the walk rounds at its first
  vertex.

Hashes of `OVWSL0.TXT` (sha256, first 8): long-a `4dfb787c`, long-b `3b14f33d`, short `52a56929`. The first prepare
put the line out of the walk's reach. Its runs are kept under `_stale-round3/v1`.

- **The long wall is held with the exact normal.** All 68 held cycles match on both carriers. Scaling the
  normal by 1.0001 already misses one, so the wall's length comes out exact and the vertex order does not
  matter. Legalism's extra unit is not the length. It stays open.
- **At a corner the walls hold the step one after the other, the nearer line first.** Heart's corner
  is at (−8.481, 181.039). The step (−171, 171) ended 1339/1024 from one wall and 1415 from the
  other, both within 1433. Held off the nearer to (−7126, 185837), it is 1518 from the other, which then
  lets it be. The original stood at (−7127, 185837). Taken the other way round, the holds give
  (−7119, 185886), where OpenVirtue stood. Implemented (`Player.HoldOffWalls`): Heart's walk now
  matches to line 354, up from 314, and stays within a unit to line 413.
- **A wall end's push scales the offset to the push length exactly and rounds each part.** The push
  length is still (c − 1)²/1024 + 102. Over the twelve carriers' cycles this misses 66, against 84 for
  the floored square root of the things' push (OV-20261103-contact-clock). The short wall's first contact,
  (4236, 148), matches now. In the replay the probe's carriers match 1827 lines, up from 1773 (the short
  wall to line 184, up from 76; f37-s03 180 lines, up from 124). w37-s03 and w45-s02-w14 each lose their
  last line.

## Round 4 (2026-11-11): far from the origin, and first contacts
Legalism's first stop (line 367) came out a unit from the hold, `y` 971780 where the rule gives 971779. Its
maze lies 1226 steps out, where products of absolute coordinates would pass 32 bits. Four carriers move
the map with `TraceProbeMap`'s new `OffsetX`/`OffsetY`. `w45-far`, `w27-far` and `w37-far` move it by
(−1200, 950), and `w45-mid` by (−300, 200). All have `PLAYER_WIDTH` 1.4. `OVWSL0.TXT` hashes: w45-far `9a0c4168`, w27-far `efb95fbc`, w37-far `b5ee5f2f`, w45-mid `fdc02910`.

- **The slide does not depend on where the map lies.** Shifted back, w45-far and w45-mid equal w45-s02-w14 on
  all 162 samples.
- The first contacts of the near-origin probes, re-read: a step that ends beyond `PLAYER_WIDTH` is not held,
  as w27-s02, w63-s02 and f45-s02 show. A hold that leaves an axis part under 4/1024 takes the minimum: w63-s02's
  (+1, +105) became (+4, +105). OpenVirtue does both.
- Legalism's stop starts 1435.9 out (`PLAYER_WIDTH` 1433, hold 1484). Only the distance left unfloored (1385.40)
  gives its (−1255787, 971780). Over all the probes' cycles, though, the floored distance misses 29 and the
  unfloored one 256. So this first contact is still open.

## Round 5 (2026-11-11): Legalism's stop rebuilt
`round5/legalism-stop` puts Legalism's maze wall in a probe room at its corners as the map gives them,
(−1195.154, 944.359) and (−1264.319, 957.931). The far corner lies outside the room. The player is beamed to
(−1256010, 971873) raw, where the walk stood a cycle before its first stop, and steps 243 raw a cycle along x.
`OVWSL0.TXT` hash `afcf60f6`.

- **The probe gives Legalism's stop exactly**: (−1255787, 971780). It then slides along the wall for about 150
  cycles. So the unit is the hold's, not the walk's.
- With OpenVirtue's hold (distance floored, both parts of the push floored), 101 of the 150 cycles are a unit
  off, all of them in y, the original one higher. Cutting the push's y part toward zero instead fits 148 of
  150 and changes no earlier probe; those all pushed y upward, where cut and floor agree. But Apathy's curtain
  slide (y pushed down, the larger part of the normal) and Heart's corner (y pushed down, the smaller part) both
  need y floored: 23 of 23 and 1 of 1 off with it cut. Neither the sign of y nor the larger part of the normal
  tells Legalism's wall from theirs.
- The difference left: Legalism's step has a y part only from the 4/1024 minimum. The probes do too, but they
  push y upward.

Not implemented; the probe is kept for the next fit.
