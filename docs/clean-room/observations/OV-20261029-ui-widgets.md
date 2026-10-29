# OV-20261029-ui-widgets: what panel bars, windows, pictures, buttons and the pointer show
Date: 2026-10-29
Status: confirmed (one round, five carriers) and implemented
Observer: OpenVirtue maintainers (black-box probe, screenshot measurement)
Implementer: OpenVirtue maintainers

## Question
Which part of its bitmap does each panel widget show (`HBAR`, `VBAR`, `WINDOW`, `PICTURE`,
`BUTTON`), and where is the `MSPRITE` mouse pointer drawn? The manual (p.125–128) says:
- a bar's bitmap is shifted by `fac·skill` pixels;
- a window shows a cutout at the skills' offsets;
- a picture shows the side the skill names;
- the pointer's `POS_X`/`POS_Y` is its hot spot.

It leaves open:
- the direction of the shift;
- fractions and values past the end;
- side 0 of a picture;
- where the pointer bitmap is drawn.

## Why It Matters
The retail HUDs show health and ammunition with bars, and the menus use windows, pictures, buttons
and the pointer.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); `SCREENSHOT` captures
  read with our own script.
- Reference material consulted: the 1998 A3 manual p.124–128.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-ui-widgets/`.
- Relevant local-only file hashes (sha256, first 8), `OVUWS0.PCX`: v0 `64718cb5`, v3 `38c0a233`, v8.6 `edd7ccf2`, v60 `2c69c270`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle check ui-widgets` (`tools/OpenVirtue.Tools/UiWidgetsProbeCases.cs`).
- Panel: at (100, 300), holding:
  - `HBAR 4,4,16` on a 62 × 6 column gradient and `VBAR 30,4,16` on a 6 × 62 row gradient, both
    with `fac` 1 and the skill `ui_v`;
  - `WINDOW 50,4,16,8` on a 62 × 32 column gradient at (`ui_v`, 0), and `WINDOW 80,4,8,16` on a
    32 × 62 row gradient at (0, `ui_v`);
  - `PICTURE 100,4` of a three-side texture at `ui_v`;
  - `BUTTON 120,4`.
- Gradients: pixel index 2 + column (or row).
- Pointer: an `MSPRITE` overlay with hot spot (2, 3) at `MOUSE_X` 400, `MOUSE_Y` 200.
- `ui_v`: 0, 1, 3, 8.6 and 60.

## Observations
| `ui_v` | HBAR / VBAR from | WINDOW X from | WINDOW Y rows | PICTURE side |
|---|---|---|---|---|
| 0 | 0 | 0 | 46–61 | 1 |
| 3 | 3 | 3 | 43–58 | 3 |
| 8.6 | 8 | 8 | 38–53 | 3 |
| 60 | 14 | 45 | 0–15 | 3 |

- The button shows its off bitmap in every case.
- The pointer bitmap covers (400…407, 200…207): its corner is at `MOUSE_X`/`MOUSE_Y`, and the hot
  spot does not shift it.

## Invariant
- **Bars.** A bar shows its bitmap from `trunc(fac·skill)`. Past the free range (bitmap size − len)
  the offset wraps around it: 60 on a 62-pixel bitmap with len 16 shows from 14.
- **`WINDOW` X.** The cutout starts at `trunc(Skillx)`, at most one pixel short of the free width.
- **`WINDOW` Y.** `Skilly` counts up from the bitmap's bottom: 0 shows the last `dy` rows.
- **`PICTURE`.** It shows side `trunc(skill)`, with 0 as the first side and values past the end
  as the last.
- **Buttons.** An idle button shows its off bitmap.
- **The pointer.** It is drawn with its corner at `MOUSE_X`/`MOUSE_Y`.

## Addendum: sliders (round 2, 2026-10-29)
- Carriers: `HSLIDER 4,20,40` and `VSLIDER 60,4,40`, both on `ui_s` (`MIN` 2, `MAX` 6), with a
  4 × 4 knob (index 150) on the panel at (100, 300). `ui_s` was set to 0, 2, 3, 4.5, 6 and 8.
  The run artifacts are in `_research/oracle-runs/OV-20261029-ui-widgets/round2/`.
- Observations:

  | `ui_s` set | read back | `HSLIDER` knob x | `VSLIDER` knob y |
  |---|---|---|---|
  | 0 | 2 | 104 | 304 |
  | 2 | 2 | 104 | 304 |
  | 3 | 3 | 113 | 313 |
  | 4.5 | 4.5 | 126 | 326 |
  | 6 | 6 | 140 | 340 |
  | 8 | 6 | 140 | 340 |

- Invariant:
  - The knob's offset is `(value − MIN)/(MAX − MIN)` of the slider's length less the knob's own
    width (height), cut to a whole pixel (22.5 → 22).
  - `MIN` is at the left or the top.
  - A `SET` outside `MIN`/`MAX` is clamped.
- Implementation:
  - `UiSliderTarget.Travel`/`OffsetFor`, and the inverse `ValueAtOffset` for dragging.
  - Tested by `UiHitTesterTests.SliderOffset_MeasuredSliders_TravelLengthLessKnobCutToAPixel`.

## Open Questions
- Whether a click happens at the hot spot (`MOUSE_X + POS_X`); OpenVirtue clicks at the pointer
  position. A headless DOSBox-X run cannot click, so this needs a visible run.
- How dragging a slider maps the pointer to a value (OpenVirtue inverts the drawing rule).
- Bars past twice the free range (wrap or subtract once), and negative values.

## Implementation
`UiOverlayBuilder` (bars, windows, the mouse sprite); tests in `UiOverlayBuilderTests` updated to
the measured rules.
