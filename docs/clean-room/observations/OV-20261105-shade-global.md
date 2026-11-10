# OV-20261105 — A texel's shade: whole zones of the player's light, one sum for the ambient

Status: confirmed (two rounds, 178 carriers) and implemented; it replaces the fitted coefficient `k`
of OV-20261029-shading and the sprite bias of OV-20261104-sprite-shade.

## Question
OV-20261029-shading fitted `30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d`, rounded down to an
even level, to carriers whose global and region `AMBIENT` were both 0, and never measured the
global skill at all. All five archives that carry a `globals.wdl` (START has none) declare
`AMBIENT` 0.3 next to `PLAYER_LIGHT` 0.2 and `LIGHT_DIST` 20, so every retail texel is shaded by a
formula tested only at `AMBIENT` 0.

Apathy's start against the original (OV-20261104-retail-frames) showed it: the curtains about 12%
too dark across x 240–376, the ratio changing by 12% in a single column at that band's edge where
the picture's wall line is ≈60.3 deep — one shade step at one depth — and the right alcove's bricks
about 7% too bright. So: what share of the level does a global `AMBIENT` take, how does it combine
with the region's and the texture's, and how does the player's light add to that base?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Fixture generator: `ovtool oracle prepare shade-global <dir>`
  (`tools/OpenVirtue.Tools/ShadeGlobalProbeCases.cs`). Synthetic level only: a room whose floor is
  the palette's 32-step gray ramp at indices 16–47, declared one `RANGE 16, 32`, so a texel's
  palette index is its shade level plus 16 (index 1 is a level past the range). One free wall from
  (24, −20) to (48, 20) stands in the room: the eye at the origin looking along +x meets it at
  x = 60/(5/3 + u), and the room's own floor is read down a column the wall leaves clear, so one
  picture holds the level at every depth from 22 to 48 steps.
- Projection (OV-20261009): vertical focal 659 px, horizontal 585.5 px, centre (320, 240), tilt 0.
  A floor row r is at depth `e·659/(r + 0.5 − 240)` for the eye height e (8, or 2 on the lowered-eye
  carriers); a wall is read along row 240 and its depth taken from the ray-wall intersection. The
  screen's level is the palette index minus 16. Traces (`OVSGL0.TXT`) print `AMBIENT`,
  `PLAYER_LIGHT`, `LIGHT_DIST` and `DARK_DIST` as the runtime holds them: 0.3 stored as 307/1024,
  0.2 as 204/1024, −0.1 as −102/1024, and `DARK_DIST` 1000.000 in every carrier.
- Round 1: eight (global, region) pairs at retail's light (p 0.2, l 20) and at a strong one
  (p 1, l 10), plus three carriers with `PLAYER_LIGHT` 0, plus a mirrored wall, a reversed one, a
  `LIGHT_DIST` 10 variant and the retail declaration `SKILL AMBIENT { VAL 0.3; }` in place of
  `SET` — 25 carriers.
- Round 2, raw readings only: block A, 13 `PLAYER_LIGHT` values (0.05 to 1) × 2 `LIGHT_DIST`
  (10, 20) × 2 eye heights, the floor down column 320 and no free wall; block B, the region
  `AMBIENT` in 1/128 steps, k 0…15, at four lights, eye at 2; block C, one free wall at a time,
  oblique and frontal, frontal half-steps made by moving the eye back with `SET PLAYER_X` so no map
  vertex is fractional — 153 carriers.
- Relevant local-only file hashes (sha256, first 8), `work/OVSGS0.PCX`: round 1 `g0a0-r`
  `b5b25b9d`, `g0a2-r` and `g3an1-r` `f260472c`, `g3a2-r`, `g3a2-dec`, `g3a2-rev`, `g0a5-r` and
  `g5a0-r` `833695ca`, `g3a2-s` `da7ddd2c`, `g3a2-ld10` `11ae18dc`, `g3a2-flip` `8829f560`,
  `g3a2-p0` `4ca50a0a`, `g3a0-r` `560b1616`; round 2 `a020l10e2` and `b020l10k00` `3b73ff63`,
  `a020l20e8` `37cf415e`, `a100l10e8` `70506831`, `a100l20e8` `9ad42ff7`, `b020l10k02`
  `d5de2546`, `w-a` `73548799`, `w-c` `640fba12`, `w-f` `f04a7558`, `w-fr255` `aa8fdde1`,
  `wr-fr33` `2d568acf`, `wr-frb60` `a856f964`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed. The retail case below is quoted as palette levels a frame draws and a
  region's mean channel values, never as data.

## Readings

### The ambient is a whole number of even levels, and the two ambients are one sum
Every lit carrier of a light set puts its boundary on the same row, to the pixel, whatever its
ambients: at retail's light the floor's 26/24 step is at d 31.76 in all of them, from total
`AMBIENT` −0.0996 to 0.5. Five of them are one file (`833695ca`): a global 0.3 with a region 0.2, a
global 0.5 with a region 0, a region 0.5 with the global 0, the wall's vertices swapped, and the
`SKILL AMBIENT { VAL 0.3; }` declaration in place of `SET`. `g0a2-r` and `g3an1-r` are one file too
(`f260472c`): 0.3 with −0.1 is 0 with 0.2.

| global / region (22.10) | total A | retail light, floor | strong light, floor |
|---|---|---|---|
| 0 / 0 | 0 | 26, 24 | 20, 18, 16 |
| 0 / −0.0996 | −0.0996 | 28, 26 | 22, 20, 18 |
| 0 / 0.1992 | 0.1992 | 20, 18 | 12, 10, 8 |
| 0.2998 / −0.0996 | 0.2002 | 20, 18 | 12, 10, 8 |
| 0.2998 / 0 | 0.2998 | 16, 14 | 10, 8, 6 |
| 0.2998 / 0.1992 | 0.499 | 10, 8 | 4, 2, 0 |
| 0 / 0.5, 0.5 / 0 | 0.5 | 10, 8 | 4, 2, 0 |

Column 320, from far to near; column 40, clear of the wall, shows one more band farther out. With
`PLAYER_LIGHT` 0 the floor is one flat level: 20 at total 0.2998, 22 at 0.2002, 14 at 0.499. Every
step in every carrier is 2 levels, and no odd level appears in any reading.

### Q: the light's zone count
Each boundary is a 2-level step, so `d·m` at a boundary reads the light's zone count `Q` (`m` the
number of zones beyond the step). One row per (p, l): the group's boundaries averaged, good to
±0.3 — how the two eye heights are pooled moves a group by up to that much, and the spread within
a group is the next paragraph's.

| p | l 10 | l 20 |
|---|---|---|
| 0.05 | 7.8 | 18.1 |
| 0.10 | 15.5 | 34.7 |
| 0.15 | 23.1 | 48.8 |
| 0.20 | 30.8 | 64.1 |
| 0.25 | 38.5 | 79.7 |
| 0.30 | 46.2 | 94.9 |
| 0.35 | 52.7 | 109.0 |
| 0.40 | 60.4 | 124.2 |
| 0.50 | 75.6 | 154.0 |
| 0.60 | 90.0 | 184.5 |
| 0.75 | 112.8 | 228.6 |
| 0.90 | 134.8 | 273.0 |
| 1.00 | 150.0 | 303.4 |

Within a group `d·m` scatters by 0.0% to 3.9% of its value (the worst is p 0.1, l 20, where the
two eyes read the same level pair at 34.69 and 33.37). It does not trend with depth: it saws with
the parity of `m`, the even zone counts about 0.3 higher than the odd, which is what a 2-level step
quantised to a scan line gives — a row is 0.17 steps at d 30 from eye 8 and 0.68 from eye 2.
Verified from `round2sweep_read.txt`: p 0.2, l 20 gives 64.30, 63.78, 64.28, 64.00, 64.32, 64.05,
64.32, 64.08, 64.30, 64.13 down column 320; p 1, l 10 gives 150.64, 149.75, 149.94, 148.82 … 150.08.

`Q − 15·p·l` is nearly constant in `p` at one light: about 0.5 at l 10 (−0.2 to 1.2) and about 4.1
at l 20 (3.1 to 4.9), the scatter of the table above. So the light's share is `15·p·l` in 1/d, and
only a small, light-dependent constant sits on top of it.

### The region ambient in 1/128ths: the offset φ
Block B steps the region `AMBIENT` by k/128 (exact in 22.10, 8k/1024). Every zone of the floor drops
one step (2 levels) between k 1 and 2 and again between k 9 and 10 at (0.2, 10), (0.5, 10) and
(1, 10), and between k 2 and 3 and 10 and 11 at (0.2, 20). The same flips hold in every zone of the
picture, and no edge row moves with k at all — the ambient moves the levels, never the depths.

The period is 8 in k for 2 levels, so `AMBIENT` counts 32 levels per 1.0, and where a flip falls
gives an offset: at l 10 the flips put it in (0.25, 0.5), and the strong light's carriers at total
A 0.1992 read a 12 band that an offset of 0.3744 or more would make 14, closing it to (0.25, 0.375);
at l 20 (0.5, 0.75). With no player light the offset is under 0.41 (total A 0.2002 reads 22 where
0.4064 would read 24). `l/32` — 0.3125 and 0.625 — sits inside both intervals.

### Walls: a frontal wall reads its whole face, an oblique one steps across the screen
Block C, p 1 and l 10, a frontal wall's whole face at D steps: 24.5, 25 → 16; 25.5, 29, 29.5, 30 →
18; 30.5, 31, 37, 37.5 → 20; 38, 38.5, 39 → 22. At retail's light, p 0.2 and l 20: 30, 31, 32 →
24; 33, 34 → 26; and 60 and 61 (seen from 14 steps back) → 26. A frontal wall is one level over its
whole width.

An oblique wall steps down as it comes nearer, at columns that do not follow its true slope. The
spacing of the steps per zone: true 271 columns and read 256 on (30, −10)-(40, 10), at least 328 on
its mirror; true 62 and read 60.3 on (20, −6)-(44, 6), 64 on its mirror. The transition columns,
with OpenVirtue's beside them:

| Wall | p, l | Columns measured | OpenVirtue |
|---|---|---|---|
| (30, −10)-(40, 10) | 1, 10 | 227.5, 483.5 | 227.5, 483.5 |
| (40, −10)-(30, 10) | 1, 10 | 138.5 | 141.5 |
| (20, −6)-(44, 6) | 1, 10 | 272.5, 332.5, 392.5, 453.5 | 273.5, 333.5, 393.5, 453.5 |
| (44, −6)-(20, 6) | 1, 10 | 179.5, 243.5, 307.5, 371.5 | 180.5, 244.5, 308.5, 372.5 |
| (24, −20)-(48, 20) | 1, 10 | 248.5, 453.5 | 248.5, 452.5 |
| (48, −20)-(24, 20) | 1, 10 | 147.5, 403.5 | 150.5, 406.5 |
| (24, −20)-(44, −4) | 1, 10 | 454.5, 600.5 | 454.5, 600.5 |
| (24, 20)-(44, 4) | 1, 10 | 31.5, 202.5 | 34.5, 205.5 |
| (30, −10)-(40, 10) | 0.2, 20 | 363.5 | 364.5 |
| (40, −10)-(30, 10) | 0.2, 20 | 294.5 | 296.5 |
| (24, −20)-(48, 20) | 0.2, 20 | 405.5 | 405.5 |
| (48, −20)-(24, 20) | 0.2, 20 | 404.5 | 408.5 |

The lag is not symmetric: on a wall whose near end is on the left the steps land up to three
columns late — four on the retail-light mirror, where the step is the smallest at 1/1024 a column —
and on a wall starting off screen they run from the true value at column 0. Both fit a slope
integrated a 1/1024th of a zone a column and rounded up — the true −16.5/1024 reads −16 and
+16.5 reads +17, −6.74 reads −6 and +6.74 reads +7, −4.40 reads −4 and +4.40 reads +5, −1.87 reads
−1 and +1.87 reads +2 — so a mirrored pair accumulates its error in opposite directions.

### Sprites, and the retail frame
The sprite probe's table (OV-20261104-sprite-shade: 20 → 16, 28 → 20, 36 → 22, 44 → 24, 28 with
the thing 12 steps aside → 20, a texture `AMBIENT` 0.25 → 12, 15.2 → 12, 19 → 16, 30.4 → 22,
38 → 24, `PLAYER_LIGHT` 0 → 30, region 0.5 → 0, `PLAYER_LIGHT` 0 with 0.25 → 22, light 0.1 with
0.25 → 22) is reproduced in full by the floor form with one zone more and the zone count floored
instead of ceiled; no separate sprite term is needed.

Retail, p 0.2 and l 20, `Qf` 64.3: the curtains (total A 0.499) lie inside 64.3 steps and so read
level 10 where the fitted formula put them at 12 — two levels brighter, which is the 12% they were
too dark by; the alcove bricks (A 0.2002) read 20 inside that depth and 22 beyond it, so the far
alcove is two levels darker than the old formula's 20, which is the shade it was too bright by. With
the change, region means, original / before / after: curtains 15.1, 13.9, 50.6 / 13.7, 12.3, 45.1 /
15.6, 14.3, 50.6; right alcove bricks 31.1, 30.1, 31.6 / 32.8, 32.0, 33.9 / 31.1, 30.2, 31.9; left
alcove bricks 25.5, 24.7, 27.0 / 25.8, 25.3, 28.3 / 25.2, 24.6, 27.1. Pixels equal to the original's
in the 3-D view 49.2% → 50.8%, in the curtains 34.6% → 45.7%; the view's mean brightness error
+1.8% → +0.1%.

## Round 3 (2026-11-10): walls nearer than LIGHT_DIST, and walls from behind the eye
Heart's start read its side walls 1.15 to 1.4 times brighter in OpenVirtue than in the original. Round 3
(`round3/`, twelve carriers) stands a side wall in the ramp colour from x −10, behind the eye, to 46 at
y ±6, in both vertex orders, at the strong light (p 1, l 10) and at retail's (p 0.2, l 20); the `-front`
ones start at x 2, ahead of the eye but off the screen. Hashes of `OVSGS0.PCX`: bh-l `7a61448c`, bh-r and
bh-r-front both `975793b6`, rbh-l `63be34fd`, rbh-r `befcc21b`. Read along row 150:

| Case | Original | OpenVirtue before |
|---|---|---|
| rbh-l (retail light, left wall) | level 22 from column 0 to 156, 24 to 213, 26 on | 18, 20, 22, 24, 26: brighter and brighter towards the edge |
| rbh-r | the mirror image | the same |
| bh-l (strong light) | steps from level 2 at the edge on | the same steps |
| bh-r against bh-r-front | **pixel for pixel the same picture** | the stepping started at column 0 for bh-r only |

- **Nearer than `LIGHT_DIST` a wall's light grows no more**: its zones are counted as if it stood at
  `LIGHT_DIST`, so retail's light gives a wall `⌈65.3/20⌉ = 4` zones, level 22, however near. At `LIGHT_DIST`
  10 no wall of the strong-light carriers was nearer than 11, so nothing showed there. A floor is not held
  so: the eye-2 floors of round 2 read down to depth 5.5 at the unclamped zones (a clamp put 45 000 to 112 000
  pixels wrong). A sprite at depth 12 reads the same either way; it is left unclamped.
- **A wall's end behind the eye counts as the wall cut just in front of it**: the stepping starts at the
  lower of the two ends' columns either way, which is what makes bh-r and bh-r-front one picture.

Heart's start against the original after this round: every 80 × 60 block within 2% in mean brightness, the
weighted difference (`magick compare -fuzz 2%`) 13 130 → 6 540; Legalism 8 859 → 8 291, New Age 9 473 → 9 132.

A refit of `Q` from round 2's floor bands, with the rows sampled at their top edge as walls are, does not
settle on one formula: `Q − 15·p·l` scatters from 0 to 1.3 at `LIGHT_DIST` 10 and from 3 to 5.5 at 20, and
`⌈K·p·l/d + c⌉` misses a fifth of the band rows at its best. The zone count is likely read from a table in
fixed point. OpenVirtue keeps the fit, a row or two off on near floors.

## Rules
- **The player's light is a whole number of zones, separate from the ambient. A floor or ceiling
  takes `m = ⌈Qf/d⌉`, a sprite `m = ⌊Qw/d⌋`, a wall `m = ⌈mw⌉` as below; the level is
  `2·⌊(30 + φ − 32·A − 2·m)/2⌋`, clamped to 0…30, and a level of 32 or more past the range is the
  `INFINITY` colour.** `d` is the depth along the line of sight, `A` the global, region's and
  texture's `AMBIENT` summed at equal weight, and `p` 0 gives `m` 0 and `φ` 0. So the ambient's
  share is always an even number of levels, the light's too, and no continuous sum
  `30 − 32·A − k·p·l/d` can reproduce a picture.
- **`Qf = 15·p·l + 4.3·(l/20)³`, `Qw = Qf + 1`, `φ = l/32`.** These are fits. `Q` is measured at
  `LIGHT_DIST` 10 and 20 only, where `Q − 15·p·l` reads about 0.5 and about 4.1: the fit's curve
  passes through 0.54 and 4.3, inside the scatter. A cube in `l/20` is the simplest form through two
  points and not a derivation, and `l/32` likewise; `Qw − Qf` rests on two lights.
- **A wall steps its zones across the screen from its first visible column:
  `mw(c) = mt(c0) + (c − c0)·r` with `r = ⌈1024·s⌉/1024`, `s` the true slope of `Qw/d` per column,
  `c0` the left end's column — or 0 when the left end is off screen.** A frontal wall has `r` 0 and
  one level over its face. The rounding up is the rule, not noise: a slope and its mirror round up
  to different magnitudes, which is why a mirrored pair's steps do not land symmetrically.
- **A global `AMBIENT` and a region's are one sum at equal weight, and so is a texture's**: 0.3 + 0.2
  reads as 0.5 + 0, 0.3 − 0.1 as 0 + 0.2, at the pixel and to the same boundary row. The
  `SKILL AMBIENT { VAL 0.3; }` declaration and `SET AMBIENT, 0.3` give identical pictures, and
  swapping a wall's two vertices changes nothing. `DARK_DIST` stays 1000 whatever the light.
- **This supersedes the coefficient `k` of OV-20261029-shading** (30.4 was a fit over a stepped
  function: the level is a staircase in 1/d, not a line) **and the sprite bias of
  OV-20261104-sprite-shade**: the sprite's "two levels less" was the floor form of the zones with
  `Qw` and a floor instead of a ceiling, which is not a constant.

## Open Questions
- A left wall's steps at retail's light land up to nine columns later in OpenVirtue (rbh-l: 166 against 157).
- `Q` and `φ` at other `LIGHT_DIST` values: the cube and `l/32` are fits through 10 and 20 and
  nothing was read at 5, 40 or retail's other values.
- `φ` at low `PLAYER_LIGHT` (the bound 0.375 comes from one p 1 carrier), and `φ` at l 20 for any p
  other than 0.2.
- Why `Qw` is one zone above `Qf` at all: the walls and the sprite table agree on it at two lights,
  and nothing derives the one.
- The columns: a near-left wall lands up to three columns (four on the retail-light mirror) late and
  the mirror pair's errors do not cancel. This probe's window is 640 columns and retail's 3-D view is
  576, and the rounding was assumed per virtual column (which is what OpenVirtue counts,
  `SCREEN_WIDTH`); a window narrower than 640 could round differently per column, and other `VIDEO`
  modes are unprobed.
- A ceiling's zones: assumed the floor's; no carrier read a ceiling. Likewise a `GENIUS` lamp's gain
  with zones (it is still additive in `A`, OV-20261104-genius), an `ATTACH`'s light (assumed its
  wall's), and a wall beside a `BELOW` stack.
- Whether occlusion, rather than the screen edge, sets a wall's `c0`: retail's long brick wall seen
  through an arch suggests it does not, but no carrier had a nearer wall cutting a farther one.
- `Q`'s sawtooth with the parity of `m` (about 0.3 of a zone): unexplained, possibly the row
  quantisation of a 2-level step rather than the engine.

## Implementation
`PaletteShading` holds the rule: `SurfaceQ` (`15·p·l + 4.3·(l/20)³`), `WallQ` (+1), `Phi`
(`l/32`), `SurfaceLevel` (⌈Qf/d⌉), `SpriteLevel` (⌊Qw/d⌋), `WallLevel` (⌈mw⌉) and `WallStep`
(`mw(c)`), with `LevelOf` the one `2·⌊(30 + φ − 32·A − 2·m)/2⌋`, `InfinityLevel` and the 0…30 clamp.
`RenderVertex` carries the wall's two end vertices in render space (`WallAX`, `WallAZ`, `WallBX`,
`WallBZ`) on every vertex of every piece and of the attach, so a near clip cannot cut the step;
`MeshBuilder` only fills them, and `GlRenderBackend`'s shader turns the two ends into columns, takes
the true slope of `Qw/d` from them and the first visible column as the lower of the two clamped to
the window, then steps the rounded slope from there and ceils where a surface takes its own
`⌈Qf/d⌉` and a sprite its `⌊Qw/d⌋`. The slope is written without dividing by `w` — `(wB − wA) /
(0.5·N·(xA·wB − xB·wA))` — so a wall end exactly on the eye's plane, where `w` is 0 and its column is
infinite, still gives a finite step; only a pair of ends on one ray degenerates, and there the slope
is 0 and the first visible column 0. `HostFrame.Zones` (`Qf`, `Qw`, `φ`) and `HostFrame.Columns`
(the 3-D window's width in virtual columns, clamped to the display as the viewport is — the space
the steps are counted in) carry the frame's light to the shader, and
`PaletteShading.SpriteMarker` marks a sprite's batch.
Tests: `PaletteShadingTests` (`SurfaceLevel_RetailLight_FallsInTheMeasuredBands`,
`SurfaceLevel_StrongLight_FallsInTheMeasuredBands`,
`SurfaceLevel_RegionAmbientIn128ths_StepsDownWhereMeasured`,
`SurfaceLevel_RetailLightAmbient_MovesTheLevelsByEightPerUnit`, `SurfaceLevel_NoLight_IsFlatInTheAmbient`,
`SpriteLevel_FloorsTheWallZones`, `WallLevel_StrongLightFrontal_StepsAtTheMeasuredDepths`,
`WallLevel_RetailLightFrontal_StepsAtTheMeasuredDepths`, `WallStep_RoundsTheSlopeUpToA1024th`,
`WallStep_MovesFromTheWallsFirstVisibleColumn`, `Phi_AddsTheLightDistanceOver32WhileTheLightBurns`,
`SurfaceQ_FitsTheMeasuredZones`, `WallQ_AddsOneZoneToTheFit`,
`Zones_AreZeroWithoutAPlayerLight_WhateverTheDistance`); `ShadeFrameTests`
(`Shade_ObliqueWalls_StepTheirZonesWhereTheOriginalDoes`,
`Shade_FrontalWalls_HoldTheMeasuredLevel`, `Shade_Floor_Steps26To24AtTheMeasuredDepth`,
`Shade_WallEndOnTheEyePlane_StaysOnTheRamp`); and
`MeshBuilderTests` (`BuildWalls_WallVertices_CarryTheWallsOwnEnds`,
`Build_SurfaceAndSpriteVertices_CarryNoWallEnds`).
