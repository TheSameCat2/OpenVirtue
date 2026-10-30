# OV-20261029-shading: how the original darkens a texel with distance and light
Date: 2026-10-29
Status: distance/light curve confirmed (two rounds, fifteen carriers) and implemented; the AUTORANGE table is OV-20261029-autorange
Observer: OpenVirtue maintainers (black-box probe, screenshot measurement)
Implementer: OpenVirtue maintainers

## Question
How does a texel's shade depend on its distance, `PLAYER_LIGHT`, `LIGHT_DIST` and `AMBIENT`, and how
does the palette's `RANGE` turn a shade into a colour? (Manual p.34–35, p.37, p.58, p.112 give the
inputs but no curve.)

## Why It Matters
OpenVirtue multiplied texture RGB by a constant light without any distance term.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); `SCREENSHOT` captures read
  as palette indices with our own script.
- Reference material consulted: the 1998 A3 manual p.34–35 (`RANGE`, `HARD`, `AUTORANGE`), p.37
  (texture `AMBIENT`), p.58 (region `AMBIENT`), p.112 (`PLAYER_LIGHT`, `LIGHT_DIST`, `DARK_DIST`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-shading/` (`round2/`).
- Relevant local-only file hashes (sha256, first 8), `OVSHS0.PCX`: light-1 `70506831`, light-0
  `65fed9bc`, region-ambient `737c3b69`, region-ambient-light `536ec571`; round2 low-p1-ld10
  `cca681d1`, low-p1-ld20 `89cdf56f`, low-p05-ld10 `fd2da491`, low-p025-ld10 `ac1357b1`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
Native Linux DOSBox-X 2026.10.01, S640x480; carriers from `ovtool oracle prepare shading`
(`tools/OpenVirtue.Tools/ShadingProbeCases.cs`). The palette holds a 32-step gray ramp at indices
16–47 (bright to black) declared `RANGE 16,32`; the side walls and the floor are index 16. Round 1
keeps the eye at 8 (the floor visible from 23 to 48 steps), round 2 lowers it to 2 (5.5 to 48). The
floor's depth at a row follows from the measured projection (OV-20261009); the shade level is the
index minus 16.

## Observations
| Case | Floor levels (from the far wall toward the eye) | Reading |
|---|---|---|
| light-0 (`PLAYER_LIGHT` 0) | 30 at every depth | the darkest level used is 30 |
| region-ambient (`AMBIENT` 0.5, light 0) | 14 | region `AMBIENT` 0.5 brightens by 16 levels |
| light-1 (`PLAYER_LIGHT` 1, `LIGHT_DIST` 10, eye 8) | 22, 20, 18, 16 from 47.7, 37.5, 29.9, 24.9 | |
| region-ambient-light | 6, 4, 2, 0 at the same depths | exactly 16 fewer |
| round2 p1-ld10 | 22 … 2, 0, boundaries at 37.66, 29.95, 24.87, 21.26, 18.83, 16.68, 14.98, 13.59, 12.55, 11.56, 10.72 | levels change by 2; linear in 1/depth |
| round2 p1-ld20, p1-ld5, p1-ld40 | the same shape with the 1/depth slope doubled, halved, quadrupled | the slope is proportional to `LIGHT_DIST` |
| round2 p075, p05, p025 (ld10), p05-ld20 | slope ×0.75, ×0.5, ×0.25; brightness keeps rising closer than `LIGHT_DIST` | the slope is proportional to `PLAYER_LIGHT` |
| `DARK_DIST` | 1000 in every case, whatever `PLAYER_LIGHT` | |
| hard (`FLAGS HARD`) | as light-1 | no difference on a sorted ramp |

Fitting each run's boundaries (the farther level's value at the boundary depth) gives intercepts of
30.0–30.5 and slopes of 29.9–31.0 × `PLAYER_LIGHT` × `LIGHT_DIST`.

## Invariant
A texel's shade level is `30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d`, rounded down to an even
level and clamped to 0…30, d being its depth along the line of sight and `AMBIENT` the region's plus
the texture's. A sorted range moves a colour that many entries along it (level 0 is the colour itself).

## Addendum (2026-10-29, oracle summary)
Every trace printed `DARK_DIST` as 1000, whatever `PLAYER_LIGHT` (0 to 1) and `LIGHT_DIST` (5 to 40)
were, although the manual says it is recalculated from `PLAYER_LIGHT`. OpenVirtue now starts it at
1000; with that the `light-*` and `low-*` traces replay line for line.

## Round 3: depth, texture and negative ambients (2026-10-30)
Three carriers ran with the eye at 2, `PLAYER_LIGHT` 1 and `LIGHT_DIST` 10, read off `OVSHS0.PCX`
(hashes neg-ambient `40616d5b`, big-ambient `430d4268`, tex-ambient `cffe6cbf`).

| Case | Floor (down the centre column) | Side walls |
|---|---|---|
| round 2 `low-p1-ld10` (reference) | 22, 20, … 0 from row 268 to 363 | 22 |
| region `AMBIENT` −0.25 | 30, 28, … 4: 8 levels darker, clamped at 30 | 30 |
| region `AMBIENT` 1 | 0 everywhere | 0 |
| the floor's texture `AMBIENT` 0.25 | 14, 12, … 0: 8 levels brighter | 22, unchanged |

- **Depth, not distance.** The round-2 screenshots already answer this. A floor boundary falls on the
  same row in every column, from x 20 to 620. A side-wall column keeps one level from row 33 to 274,
  rows up to about 14 steps above and below the eye. A Euclidean distance would bend the first and
  break the second, so d is the depth along the line of sight for floors and walls.
- **`AMBIENT`.** A texture's `AMBIENT` adds to its region's. Negative values darken, and the level is
  clamped to 0…30 at both ends.
- **Walls.** A wall between the room (`AMBIENT` −0.25) and the solid region (0) took the room's −0.25,
  its left region's: the brighter side does not win.

## Round 4: `ALBEDO` (2026-10-30)
`round4/albedo` (screenshot hash `737c3b69`) is `region-ambient` with `ALBEDO 1` in the room walls'
textures: `PLAYER_LIGHT` 0, region `AMBIENT` 0.5.
- **No effect.** Its screenshot is identical to `region-ambient`'s, pixel for pixel. Without a light
  source `ALBEDO` changes nothing, and OpenVirtue ignores it, as before.
- **No `LIGHT_ANGLE`.** The skill the manual names as the light's direction (p.33) does not exist
  in V3.963: `PRINT_VALUE LIGHT_ANGLE` stops the compile with "Error(63): Parameter unknown". Cases
  that set it could not run.
- **`GENIUS` lights the walls.** `round4/genius` (hash `8ef1b4ea`) adds `GENIUS sh_lamp` to the room.
  `sh_lamp` is an invisible thing at (20, −20), near the right wall, whose texture has `AMBIENT 1`.
  - **Left side wall.** It faces the lamp from 44 steps away and goes from shade level 14 to 0.
  - **Right side wall.** It is 4 steps from the lamp, which it sees at a grazing angle, and goes
    from 14 to 2.
  - **Floor.** It does not change.
  - **Not enough for a curve.** Two walls, one of them clamped at full brightness, cannot settle
    the formula; cases with a smaller `ALBEDO` and other lamp positions are needed. OpenVirtue does
    not model `GENIUS` light.

## Round 5: `GENIUS` positions (2026-10-30)
Four cases use the walls' `ALBEDO` 0.25 and move the lamp. Only the side walls' far ends (x 44–48)
show in the screenshots. Shade levels, where region `AMBIENT` 0.5 alone gives 14:

| Lamp | `ALBEDO` | Left wall (y 24) | Right wall (y −24) |
|---|---|---|---|
| (20, −20) | 1 (round 4) | 0 | 2 |
| (20, −20) | 0.25 | 10 | 10 |
| (20, 20) | 0.25 | 10 | 10 |
| (44, 0) | 0.25 | 10 | 10 |
| (2, −20) | 0.25 | 10 | 12 |

- **Scale.** Facing the lamp, a wall gains `ALBEDO` × 16 levels.
- **Angle.** The grazing cases gain less: the right wall with the lamp at (2, −20), and with
  `ALBEDO` 1 at (20, −20). A cosine at each wall point does not fit them. The rule may be one value
  a wall segment, from the segment's midpoint, but these screenshots cannot show it.
- **What OpenVirtue does.** The first-order rule: a wall whose left region names a `GENIUS` with a
  lit texture gains `ALBEDO` × 0.5 of ambient (16 levels a 1.0) when the lamp stands on that face's
  side (`MeshBuilder.GeniusLight`). The grazing falloff is not modelled. apathy uses `GENIUS` 13
  times and `ALBEDO` 33 times.

## Open Questions
- Answered by OV-20261029-autorange: the `AUTORANGE` table (a 6-bit fade toward the `INFINITY` colour,
  then the nearest colour) and sorted ranges of other lengths (`⌊(n + 1 − i)·L/32⌋` places).
- Answered in round 3: d is the depth, texture `AMBIENT` adds, negative ambients darken.
- `GENIUS` light (round 4: `ALBEDO` alone does nothing, `LIGHT_ANGLE` is unknown), `HARD` on an
  unsorted palette, and a wall seen from its right region.

## Implementation
`OpenVirtue.Engine.Rendering.PaletteShading` (`Level`, `BuildTable`), `MeshBuilder.WallAmbient` (the left region's `AMBIENT`), the palette `RANGE`/`FLAGS`
in `Level.PaletteShading`, palette indices in `TextureImage`, and the GL backend's shaded path
(index texture → shade table → palette, level from the clip-space depth). Tests: `PaletteShadingTests`
reproduce the measured levels.
