# OV-20261104 — The retail title screens, frame against frame

Status: confirmed (one run of the retail title under the DOS runtime, three screens) and implemented.

## Question
Every rendering rule so far was measured on synthetic levels. Does the retail title — its logo
flic, splash and menu — come out of OpenVirtue as the original draws it, pixel for pixel, and
where not, why?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 under `Xvfb` (`SDL_VIDEODRIVER=x11`, `forcerate=60`),
  the SaintsX-patched `VRUN.EXE` V3.963 running a copy of the user's own `TITLE.WRS` with its
  `SET APPEND_MODE` lines commented out (a V3.990 keyword the DOS runtime rejects; the save-body
  tests patched the same), `VRUN.EXE -NJ TITLE -RUN`, no keys; the X screen grabbed every two
  seconds with ImageMagick `import` and the 640×480 window cropped out (local-only under
  `_research/oracle-runs/OV-20261104-retail-frames/title/`; nothing of it is committed).
- OpenVirtue: `RetailSmokeTests` (the GL host, `title-2-splash.png` and `title-3-menu.png`
  under the temp directory), compared with `magick compare` (`-fuzz 6%` pixel count, RMSE) and
  sampled pixel pairs.
- Confirmation that no proprietary code/assets are committed: the grabs and the patched archive
  stay under `_research/`.

## Readings
| Screen | Reading |
|---|---|
| logo flic | the original's frame a green Conitec-style logo on black; OpenVirtue's capture lands on another frame of the same flic (timing, not compared) |
| splash | 0 of 307200 pixels differ at a 6 % fuzz; RMSE 0.4 %: the same picture |
| menu (the splash under the gray fade, red text, the cursor at New Game) | 3470 pixels (1.1 %) differ at 6 %, RMSE 3.8 %: the border left, right and above the 3-D window (`SCREEN_X/Y` 32, 576×432) reads 4, 4, 4 in the original and 20, 26, 36 in OpenVirtue; inside the window grays read 48 / 28 / 12 in the original where OpenVirtue has 50 / 30 / 14, and 40 / 60 alike; the text and cursor the same |

- **The border around a 3-D window smaller than the screen is palette entry `FRAME_COLOR`** (the
  title sets 0, black), shown through the current palette and fade: 4, 4, 4 under the menu's gray
  fade. OpenVirtue cleared to its own dark blue.
- **Every pixel goes through the 6-bit DAC**: a gray of 50 reads 48 (12 widened), 30 reads 28.
  OpenVirtue's RGB-drawn pixels (panels, texts, unshaded levels) kept 8-bit values; its
  palette-drawn ones were already widened (OV-20261104-fade-pal).

## Open Questions
- The in-game screens (Apathy's start: walls, floors, the HUD and its texts) against the
  original, which needs keys pressed into the DOS run (`AUTOTYPE`), and the flic frames.

## Implementation
`RuntimeHost.TintedClearColor` clears to the shown palette's `FRAME_COLOR` entry when the level
declares a palette; the GL shader's RGB path cuts its output to 6 bits a channel and widens it as
the emulator shows the DAC. Tests: `RuntimeHostPaletteFadeTests.Frame_ClearsToThePalettesFrameColorEntry`.
