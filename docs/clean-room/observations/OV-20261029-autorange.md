# OV-20261029-autorange: the shade table the original builds from a palette
Date: 2026-10-29
Status: confirmed (one round, 32 carriers) and implemented
Observer: OpenVirtue maintainers (black-box probe, screenshot measurement)
Implementer: OpenVirtue maintainers

## Question
Which palette index does the original draw for colour k at shade level L (0–30)?
- For a sorted `RANGE` of any length, how far along the range does a colour move?
- With `FLAGS AUTORANGE`, what does "shaded according to their similarity to the INFINITY colour"
  (manual p.35) mean? The retail palettes use `RANGE 3, 253` with `AUTORANGE`.

## Why It Matters
This table colours every shaded texel. OpenVirtue guessed a proportional move along sorted ranges.
For `AUTORANGE` it guessed an exact-fraction fade, then the nearest colour. Both give visibly
different colours from the original.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); `SCREENSHOT` captures
  read with our own script (palette index at each grid cell).
- Reference material consulted: the 1998 A3 manual p.34–35 (`RANGE`, `AUTORANGE`, the `INFINITY`
  colour #1).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-autorange/`.
- Relevant local-only file hashes (sha256, first 8), `OVARS0.PCX`: auto-00 `73fda054`, auto-16
  `a594c202`, auto-30 `db35a079`, sorted-00 `f4e728ea`, sorted-16 `5493395c`, sorted-30 `93fdfc3f`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers and synthetic
  palettes only.

## Setup
- Emulator: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`. The carriers come from
  `ovtool oracle prepare autorange` (`tools/OpenVirtue.Tools/AutorangeProbeCases.cs`).
- Grid: the far wall's square target face is a 16 × 16 grid of all 256 palette indices, about
  12 pixels a cell.
- Uniform level: `PLAYER_LIGHT` 0 and the room's `AMBIENT` (30 − L)/32 put every texel on level
  L (OV-20261029-shading). One screenshot per even level 0–30 reads a whole level of the table.
- Two palettes:
  - **auto**: pseudo-random colours at 2–255 (a fixed LCG), `INFINITY` (32, 48, 96),
    `RANGE 2,254; FLAGS AUTORANGE`.
  - **sorted**: gray, red, green and blue ramps from bright to black, as
    `RANGE 16,32; RANGE 48,16; RANGE 64,8; RANGE 80,64`. `INFINITY` is black and all other
    colours are mid-gray.

## Observations
- At level 0 every cell shows its own index. Colours outside every range never change.
- **Sorted ramps.** Each colour moves along its own ramp, by an amount that falls with its place
  in the ramp.
  - The first two colours always move together: L places in a 32-ramp, L/2 in a 16-ramp, 2L in
    a 64-ramp.
  - The move then drops by one every 32/L places.
  - The darkest colours reach the ramp's end and show index 1, though the ramp's last colour has
    the same RGB.
  - All 1920 ramp entries fit `move = ⌊(n + 1 − i)·L / 32⌋` for place i in a ramp of length n.
    A colour that would move to place n or beyond shows index 1.
- **AUTORANGE.** Each colour drifts toward the `INFINITY` colour through nearby palette colours.
  The bright green (0, 232, 147), for example, goes to (3, 208, 134), then (7, 130, 106), then
  (3, 95, 93), then index 1.
  - An exact-fraction fade toward `INFINITY` with the nearest colour fits only 89 % of the cells,
    at any fraction.
  - All 4064 cells fit a fade in whole 6-bit VGA steps, rounded down:
    `t = (c·(32 − L) + I·L) >> 5` per channel. The nearest colour to t follows, by squared
    6-bit RGB distance, the lower index on a tie.
  - Example: colour 7 (32, 17, 44) at level 2 fades to (30, 16, 42). That is nearer colour 69
    (31, 14, 43) than colour 7 itself, and the original drew 69.

## Invariant
The shade table holds, for each palette index and level L (0–31), the index drawn. Index 1 is the
`INFINITY` colour.
- **Unshaded colours.** A colour outside every `RANGE` is drawn unchanged.
- **Sorted range** (no `AUTORANGE`). The colour at place i of a range of length n moves
  `⌊(n + 1 − i)·L / 32⌋` places toward the range's last colour. A move to place n or beyond
  gives index 1.
- **`AUTORANGE`.** Each channel of the colour (top 6 bits) is faded to
  `(c·(32 − L) + infinity·L) >> 5`. The table takes the colour of the ranges, or index 1, with
  the least squared RGB distance to the result, the lower index on a tie.

## Open Questions
- `FLAGS HARD`: the manual says colours are then "merely shifted". The sorted rule above is
  already a pure shift, so `HARD` probably changes something else (the shading probe's `hard` case
  is not yet analysed).
- Overlapping ranges, and whether level 0 maps a duplicate colour to a lower index under
  `AUTORANGE` (the synthetic palette had no duplicates).

## Implementation
`OpenVirtue.Engine.Rendering.PaletteShading.BuildTable`. A script check reproduces all 4096 cells
of both measured tables. Tests: `PaletteShadingTests.BuildTable_SortedRange_MovesEachColourAsMeasured`
and `BuildTable_AutoRange_FadesInWholeSixBitSteps`.
