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

## Open Questions
- Answered by OV-20261029-autorange: the `AUTORANGE` table (a 6-bit fade toward the `INFINITY` colour,
  then the nearest colour) and sorted ranges of other lengths (`⌊(n + 1 − i)·L/32⌋` places).
- Texture `AMBIENT` and negative ambients, `ALBEDO`/`GENIUS` light, `HARD` on an unsorted palette, and whether d is the depth or the Euclidean distance off the screen centre.

## Implementation
`OpenVirtue.Engine.Rendering.PaletteShading` (`Level`, `BuildTable`), the palette `RANGE`/`FLAGS`
in `Level.PaletteShading`, palette indices in `TextureImage`, and the GL backend's shaded path
(index texture → shade table → palette, level from the clip-space depth). Tests: `PaletteShadingTests`
reproduce the measured levels.
