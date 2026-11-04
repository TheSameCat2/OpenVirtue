# OV-20261104 — FADE_PAL: the palette the screen shows

Status: confirmed (nine screenshot carriers, two carriers grabbed from the emulator's window: the per-entry rule over all 256 entries) and implemented.

## Question
The retail title fades every level in from and out to a uniform near-black palette
(`FADE_PAL pal_black, 0.95` stepping down to 0 at a level's start, up to 1 before `LEVEL`),
flashes a uniform red on damage and tints its menus a uniform blue, all with plain factors. The
manual (pp. 83–84) says `FADE_PAL palette, factor` "sets or changes the actual palette by fading"
toward the named palette by a factor of 0..1, that 1 or above "makes the given palette the new
current palette", and that a level palette must be declared *after* a black one. Which declared
palette does the screen show at the start, what does a fraction do to each entry, is a fade taken
from the current palette or from the blend before it, what does 0 restore after a 1, and what of
factors outside 0..1?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01, `forcerate=60`, the SaintsX-patched `VRUN.EXE`
  V3.963; the screenshot carriers with `SDL_VIDEODRIVER=dummy`, the `hold` carrier with a
  visible window and the `swatch` carrier under `Xvfb`, their windows grabbed every two seconds
  (`grim` on the desktop, ImageMagick `import` on the X server).
- Fixture generator: `ovtool oracle prepare fade-pal <dir>` (`tools/OpenVirtue.Tools/FadePalProbeCases.cs`).
  The level declares `probe_pal` (PRBPAL.PCX, the textures' palette) first and then
  `fp_blk` (every entry 0, 0, 0), `fp_red` (every entry 200, 30, 15, the retail damage red) and
  `fp_rmp` (entry i = i, 255 − i, 128); the swatch case adds `fp_uni` last (R = 4·(i mod 64),
  G = 84·(i div 64), B = 4·(63 − i mod 64), every entry told apart on the 6-bit DAC) and a free
  wall textured with the 16×16 swatch of every index. Each case runs `FADE_PAL`s with a
  `SCREENSHOT` two ticks after each (`OVFPS<n>.PCX`), and the screenshots' palettes are read
  against the palette files. Traces (`OVFPL0.TXT`, sha256 first 8): start `dce49209`, black
  `040869cc`, ramp `4322e4a4`, red `de43bdff`, timing `84f6ee5c`, blur `6d780850`, range-half
  `b539619e`, range-all `759770e3`, range-auto `755e8efc`, hold `e621f145`, swatch `a6f60a67`.
- Confirmation that no proprietary code/assets are committed: synthetic level and palettes only;
  nothing under `_research/` is committed.

## Readings
A screenshot's palette carries each channel cut to the DAC's 6 bits (255 reads 252, 230 reads
228, 30 reads 28, 15 reads 12). The window grabs show the emulator's 8-bit rendering of the DAC
level d as 4d + d/16 (63 reads 255, 55 reads 223, 50 reads 203, 30 reads 121).

| Carrier | Reading |
|---|---|
| start | no `FADE_PAL`: the screenshot carries the ramp, the last declared palette, not `probe_pal` |
| black | `fp_blk` at 0.25, 0.5 and 0.75: the ramp unchanged in every screenshot; at 1: black; then at 0 and at 0.5: still black |
| ramp | `fp_rmp` at 0.5, 0.3 and 1, `fp_blk` at 0.5 and 0: the ramp in every screenshot; `probe_pal` at 1: the level palette; `fp_rmp` at 0: the level palette still |
| red | `fp_red` at 0.9, 0, 0.3 and `fp_blk` at 0.3: the ramp; `fp_red` at 1.5: every entry 200, 28, 12; at −0.5 and 0.125: the red still |
| timing | `fp_blk` 0.5 with a screenshot in the same cycle, one tick later, and 0.75 after a `WAIT 1`: the ramp in all three |
| blur | the level palette with `FLAGS BLUR` declared first, `MOTION_BLUR` 1, 0.5 and 0: every screenshot 640×480, the ramp |
| range-half, range-all, range-auto | `probe_pal` with `RANGE 16, 32`, `RANGE 2, 254` and the latter with `FLAGS AUTORANGE`, made current at 1, then `fp_blk` 0.5 and 0.25, `fp_red` 0.5, `fp_blk` 0: the level palette in every screenshot |
| hold (window) | `probe_pal` at 1: floor 121,121,121 (level 30), ceiling 60,60,60 (15), red wall 223,32,32 (55, 8, 8), yellow 243,223,40 (60, 55, 10), green 32,203,32 (8, 50, 8); `fp_blk` 0.5: 60,60,60 (15), 28,28,28 (7), 109,16,16 (27, 4, 4), 121,109,20 (30, 27, 5), 16,101,16 (4, 25, 4); `fp_red` 0.5 after it: 162,73,65 (40, 18, 16), 130,44,36 (32, 11, 9), 211,28,20 (52, 7, 5), 223,125,24 (55, 31, 6), 117,113,20 (29, 28, 5); `fp_blk` 0: the level palette again |
| swatch (X server) | `fp_uni` current, every entry on the swatch wall: `fp_blk` 0.5 reads every channel halved, rounded down (768 of 768 values); `fp_red` 0.3 every value floor(c + f·(t − c)) with f in [0.2982, 0.3) (307/1024 fits, 76/256 and 77/256 do not); `fp_red` 0.9 with f in [0.8983, 0.9): 921/1024 fits all 768, 922/1024 fails 48 (0 toward 50 reads 44, 60 toward 50 reads 51); `fp_rmp` 0.5 (a target with every entry different) every value floor(c + (t − c)/2); `fp_blk` 0: `fp_uni` again |

- **The screen shows the last declared `PALETTE` at a level's start.** The retail levels declare
  `levelPal` after every include; the title declares its black "start pal" last and fades to
  `levelPal` with a factor of 3.
- **A factor of 1 or above makes the named palette the current one; 0 or below shows the
  current palette again.** There is no memory of an earlier palette: after `fp_blk` at 1 a 0
  leaves the screen black, and `probe_pal` has to be named to get the level palette back.
- **A fraction blends every entry from the current palette toward the named one, in the DAC's
  6-bit units, rounded down**: level + f·(target − level) with 30 → 40 toward 50 at 0.5, 15 → 32
  (32.5) and 8 → 5 (5.5) toward 3. The blend is taken from the current palette, not from the
  blend before it: half red after half black reads 40 for the floor, not 32. The factor is the
  script's 22.10 value, the literal 0.9 cut toward zero to 921/1024 (0 toward 50 reads 44, not
  the 45 of 922/1024); the arithmetic is in 6-bit units, not in 8-bit values cut afterwards
  (that fits 932 of 1000 values of the 0.3 fade; the swatch separates the two).
- **Each `FADE_PAL` replaces the last**: `fp_red` 0.5 after `fp_blk` 0.5 shows red only.
- **`SCREENSHOT` saves the current palette, never the blend.** Nine carriers' screenshots showed
  no fraction at all, with or without shading `RANGE`s and `AUTORANGE`; the window grabs show
  the blend. The screenshot is 640×480 with `MOTION_BLUR` 1 or 0.5 as with 0 (the manual's "size
  depends on the current MOTION_BLUR" did not show; the current palette there carried no `BLUR`).

## Open Questions
- What a fade to a palette with `FLAGS BLUR` or a factor of 2 or 3 recomputes (the manual's
  shading and blur tables); `MOTION_BLUR` itself.
- Whether a flic's palette replaces the current palette and what the fade state is after it.

## Implementation
`PaletteFade` (`src/OpenVirtue.Engine/Rendering/PaletteFade.cs`): the shown palette as the
current one blended toward the target in 6-bit units, rounded down, widened as the emulator shows
it. The host (`RuntimeHost.LoadPalettes`, `ApplyPaletteFade`, `RefreshPaletteFade`) reads every
declared palette's file, starts on the last declared, makes the named palette current at a factor
of 1 or above, keeps one blend (target, factor) that each request replaces, and uploads the shown
palette with the shade table so batches drawn through the palette show the fade exactly; batches
drawn in RGB (levels without a `RANGE`, panels, texts) mix toward the current palette's colour
when it is uniform and then toward a uniform target (`PaletteFadeTint`, the GL fragment shader).
Tests: `PaletteFadeTests`, `RuntimeHostPaletteFadeTests`.
