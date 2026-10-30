# OV-20261029-ui-layout: where DIGITS and TEXT characters go
Date: 2026-10-29
Status: confirmed (one round, nine carriers) and implemented
Observer: OpenVirtue maintainers (black-box probe, screenshot measurement)
Implementer: OpenVirtue maintainers

## Question
How does the original lay out a panel's `DIGITS` and a `TEXT`? The manual (p.126–131) gives the
positions of panels and texts. It does not say:
- how `DIGITS` places a number in its `len` places;
- what it shows for a fraction, a number too long, or a minus in a digit-only font;
- how `CENTER_X`, `CENTER_Y`, `CONDENSED` and `NARROW` place the characters.

## Why It Matters
Every retail HUD counter (health, ammunition, score) is a `DIGITS` element. Menus and messages are
`TEXT`.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); `SCREENSHOT` captures read
  with our own script, palette index per pixel.
- Reference material consulted: the 1998 A3 manual p.27 (`FONT`), p.126–131 (`PANEL`, `DIGITS`,
  `TEXT`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-ui-layout/`.
- Relevant local-only file hashes (sha256, first 8), `OVULS0.PCX`: v7 `deb3d761`, v-56 `e8955d6a`,
  v3.7 `8c9ddef8`, condensed `9b3f2645`, center `3ff27603`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers and synthetic
  fonts only.

## Setup
- Carriers: `ovtool oracle check ui-layout` (`tools/OpenVirtue.Tools/UiLayoutProbeCases.cs`).
- Fonts: an 8 × 8 font of 128 characters with glyph c in palette index 64 + c, and an 11-character
  digit font with place k in index 230 + k (10 is the space).
- Panel: a 64 × 24 panel bitmap (index 250) at (100, 60) with `FLAGS VISIBLE, REFRESH`. It has
  `DIGITS 4,4,5` in the digit font and `DIGITS 4,14,5` in the text font, both showing `ui_v`.
- Text: a `TEXT` at (300, 200) showing a string.
- The cases vary `ui_v` and the text's flags.

## Observations
| Case | Digit font, 5 places from x 104 | Text font, from x 104 |
|---|---|---|
| 7 | 4 spaces, then 7 at 136 | the same |
| 1234 | space, then 1 2 3 4 | the same |
| −56 | 3 spaces, 5, 6 (no minus) | 2 spaces, −, 5, 6 |
| 3.7 | 4 spaces, then **4** | the same |
| 123456 | 2 3 4 5 6 | the same |
| 0 | 4 spaces, then 0 | the same |

- **Spaces are drawn.** The spaces in front are the fonts' space glyphs, drawn over the panel.
- **Plain text.** "Ab -9" at (300, 200): characters every 8 pixels from 300.
- **`CENTER_X`, `CENTER_Y`.** "Ab" starts at (292, 196), half its 16 × 8 size up and left.
- **`CONDENSED`.** "AAAA" covers x 300–328: the glyphs step 7 pixels but each is drawn 8 wide.
- **`NARROW`.** It covers x 300–325: the glyphs step 6 pixels, each drawn 8 wide.

## Invariant
- **`DIGITS`.**
  - The value times `fac` is **rounded**, then right-aligned in `len` places, with the font's space
    in front.
  - A longer number keeps its last `len` digits.
  - A minus takes a place, and a font without one draws the space there.
- **`TEXT` characters.** They advance by the font width (`CONDENSED` one pixel less, `NARROW` two
  less) and are drawn at full width, each over the one before.
- **Centring.** `CENTER_X` and `CENTER_Y` centre the text block on `POS_X` and `POS_Y`.

## Open Questions
- Rounding of exact halves (OpenVirtue rounds away from zero).
- Multi-line `TEXT` (`STRINGS`, `OFFSET_Y`, `SIZE_Y` clipping), 256-character fonts, and
  `RELPOS`/`ABSPOS` with a 3-D window smaller than the screen.

## Implementation
- `UiTextBuilder.FormatDigits` (rounding), `TextDrawWidth` (full-width glyphs) and the host's
  `FontAtlasIndex` (the minus as a space in a digit font).
- Tests: `UiTextBuilderTests.Build_PanelDigits_ShowTheMeasuredLayout` and the updated condensed
  test. A script compared OpenVirtue's glyph positions with every screenshot.
