# OV-20261104 — The retail title screens, frame against frame

Status: confirmed (two runs of the retail title under the DOS runtime, four screens) and
implemented; its Apathy start frame was re-measured on 2026-11-05.

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
- OpenVirtue: `RetailSmokeTests` (the GL host, `title-2-splash.png`, `title-3-menu.png` and
  `apathy-start.png` under the temp directory), compared with `magick compare` (`-fuzz 6%`
  pixel count, RMSE) and sampled pixel pairs. A second run typed `AUTOTYPE -w 28 -p 2 enter
  enter` at the menu to start Apathy on the default difficulty, from a local copy of
  `apathy.wrs` with its `APPEND_MODE` lines commented out; `ovtool level info <wrs> <wdl>
  --sprites` lists the sprites OpenVirtue builds from the start with their bearing and distance.
- Confirmation that no proprietary code/assets are committed: the grabs and the patched archive
  stay under `_research/`.

## Readings
| Screen | Reading |
|---|---|
| logo flic | the original's frame a green Conitec-style logo on black; OpenVirtue's capture lands on another frame of the same flic (timing, not compared) |
| splash | 0 of 307200 pixels differ at a 6 % fuzz; RMSE 0.4 %: the same picture |
| menu (the splash under the gray fade, red text, the cursor at New Game) | 3470 pixels (1.1 %) differ at 6 %, RMSE 3.8 %: the border left, right and above the 3-D window (`SCREEN_X/Y` 32, 576×432) reads 4, 4, 4 in the original and 20, 26, 36 in OpenVirtue; inside the window grays read 48 / 28 / 12 in the original where OpenVirtue has 50 / 30 / 14, and 40 / 60 alike; the text and cursor the same |
| Apathy's start (the amphitheatre from the stage, the intro text, the HUD) | 6576 pixels (2.1 %) differ at 6 % before the fixes below: the HUD's fourth slot holds the scroll icon in the original and is empty in OpenVirtue (`PICTURE 136, 5, scrollIconTex, 0`, a literal where OpenVirtue wanted a skill name); the intro's lines 2–4 start 4 pixels further left in the original (141, 149, 189 against 145, 153, 193) while lines 1, 5 and 6 match, the three being the lines whose source ends in a raw carriage return before the line feed, so the original counts the CR as a character of the line; the seat rows' and curtain stripes' edges differ by a pixel here and there; two small green sprites float above the brick blocks and the left alcove shows a dark passage with a pedestal in the original only (see the open questions). After the fixes the text columns match exactly and the scroll draws: 3379 pixels (1.1 %) differ, the texture edges, the two blobs and the alcove. |

- **The border around a 3-D window smaller than the screen is palette entry `FRAME_COLOR`** (the
  title sets 0, black), shown through the current palette and fade: 4, 4, 4 under the menu's gray
  fade. OpenVirtue cleared to its own dark blue.
- **Every pixel goes through the 6-bit DAC**: a gray of 50 reads 48 (12 widened), 30 reads 28.
  OpenVirtue's RGB-drawn pixels (panels, texts, unshaded levels) kept 8-bit values; its
  palette-drawn ones were already widened (OV-20261104-fade-pal).

- **A panel `PICTURE`'s fourth argument may be a number**: a literal 0 shows the first frame.
- **A raw carriage return inside a string counts as a character of its line**, blank but a cell
  wide, so a centred line ending in CR LF sits half a cell further left than its text alone.

## Round 2 (2026-11-05): the wall pieces and the row mask
A second look with the wall-pieces and wall-light rules (OV-20261105-wall-pieces,
OV-20261105-wall-light) explains the start frame's three largest differences.

- **The two green blobs are the original's indicator attaches**, `ATTACH` textures on walls
  between two regions, which OpenVirtue drew on a one-region wall only. They are bright green in
  one sampled frame and dark green in the next: one attach whose own texture has two `CYCLES`.
- **The "dark passage with a pedestal" is the left alcove's far wall**, which OpenVirtue already
  drew; it read too bright because a wall took its left region's `AMBIENT` where the original
  takes the ambient of the region on the side the piece faces.
- **The seat rows' stripes were not sub-texel edges but two rules**: `PORTCULLIS` anchoring a
  two-piece wall's texture at each piece's own edge, and the bit mask a wall bitmap's rows are
  read through (the seats' 120-row curtain reads `v & 119`, a period of 128 rows).

Measured against the same original frame (`../OV-20261104-retail-frames/apathy/cmp/orig-text.png`,
frames `ov.png` and `ov3.png`, the latter from the final build): `magick compare -metric AE
-fuzz 6%` 3408 → 2893 and `-fuzz 15%` 1469 → 1073 (the same weighted-distance sum the figures
above quote, not a pixel count, so this frame's earlier 3379 is the same measurement from
before); a strict count of the pixels differing by more than 6% of the range, 56828 → 55433 of
307 200. Region means (original / before / after):

| Region | Original | Before | After |
|---|---|---|---|
| seat rows left | 18.4, 19.6, 19.1 | 14.7, 12.8, 12.2 | 18.4, 19.7, 19.2 |
| left alcove bricks | 25.5, 24.7, 27.0 | 30.0, 29.3, 32.1 | 25.8, 25.3, 28.3 |
| right alcove bricks | 31.1, 30.1, 31.6 | 32.8, 32.0, 33.9 | 32.8, 32.0, 33.9 |
| curtains | 15.1, 13.9, 50.6 | 13.7, 12.3, 45.1 | 13.7, 12.3, 45.1 |

Still open: the curtains (about 1.5 shades, and 5.5 in the last band), the right alcove's bricks
(about one shade), the right-hand seat rows' bright bands sitting a row or two off in places, and
the sub-pixel edges the earlier note named.

## Open Questions
- The curtains' shades (15.1, 13.9 and 50.6 in the original against 13.7, 12.3 and 45.1) and the
  right alcove's bricks, about one shade; the flic frames.
- Sub-pixel texture edges, if any remain after the wall rules above.

## Implementation
`RuntimeHost.TintedClearColor` clears to the shown palette's `FRAME_COLOR` entry when the level
declares a palette; the GL shader's RGB path cuts its output to 6 bits a channel and widens it as
the emulator shows the DAC; `UiOverlayBuilder` takes a number as a `PICTURE`'s frame selector;
`UiTextBuilder` keeps a carriage return as a glyph of its line. Tests:
`RuntimeHostPaletteFadeTests.Frame_ClearsToThePalettesFrameColorEntry`,
`UiOverlayBuilderTests.BuildPanelWidgets_PictureWithALiteralSelector_DrawsTheFirstFrame`,
`UiTextBuilderTests.Build_CarriageReturnInAString_CountsAsABlankCharacter`.
