# OV-20261106-screen-frame: The screen is one persistent picture
Date: 2026-11-06
Status: confirmed and implemented
Observer: Claude Code (Opus 5.5)
Implementer: same

## Question
The manual says a panel without `REFRESH` is redrawn only when a skill it shows changes (p.125), and
retail levels run `SET FRAME_COLOR, 1; WAIT 2; SET FRAME_COLOR, 0;` under the comment "Clear frame to
black". OpenVirtue cleared the window and drew every element on every frame. What does the original
keep from frame to frame, what does `FRAME_COLOR` paint and when, and when is a panel drawn at all?

## Why It Matters
Apathy's surround reads palette entry 1 (OV-20261104-retail-frames round 3 measured it as an
unexplained 4, 4, 4). Every retail UI effect that depends on a repaint (a hidden panel, a moved text,
a pointer's trail, RENDER_MODE 0) follows from this.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Reference material consulted: the 1998 manual p.125–126 (`REFRESH`), p.104 (`RENDER_MODE`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261106-screen-frame/` (round 1) and `round2/`.
- Relevant local-only file hashes (sha256, first 8), `OVFRS1.PCX`: shrink and panel-in `720b34b2`,
  paint `59b84824`, panel-out `cf98cbc9`, digits-out `77fb481f`, text-over `a6103884`, overlay-out
  `7c844ed6`, hold-panel `b971317d`, top-row `bf72c9b2`, pointer `6642ba76`; round 2, `OVFRS2.PCX`:
  k-vis `6a356289`, k-dig `f7b6e731`, k-hold `e86f734e`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Method
`ovtool oracle prepare screen-frame` (`tools/OpenVirtue.Tools/ScreenFrameProbeCases.cs`): the template
room with a 64×24 panel of index 250, a font whose glyph c is index 64 + c, a digit font 230 + k, frame
colours 200 and 201 no room ink uses. Each step takes a numbered `SCREENSHOT`, read as palette indices.
The small window is (32, 32) 576×388. Round 2 takes one screenshot a cycle after a change.

## Observations
| Case | Reading |
|---|---|
| shrink | the window shrinks with `FRAME_COLOR` 0: the old view stays in the border |
| hold | `FRAME_COLOR` 200 held: the border reads 200 on every frame, outside the window whatever size it has |
| paint | 200 for two ticks, then 0: the border keeps 200; a smaller window leaves the old view in the ring it gave up, and a later paint covers that |
| paint-same | 200 and 0 in one cycle: nothing painted |
| panel-out, no `REFRESH` | after a paint the panel is gone; hiding draws nothing; `VISIBLE` 1 draws it; a move draws it again, leaving the old one (x 4–77); the next paint removes all |
| panel-out-r, `REFRESH` | drawn on every frame, but hiding or moving leaves the old one on the border until a paint |
| text-out | drawn on every frame; hidden or moved it stays until a paint; a shorter string leaves the longer one's tail |
| overlay-out | at (4, 4) it reads (36, 36): an overlay is inside the 3-D window unless `ABSPOS` (manual p.125), and clean there |
| hold-panel | `FRAME_COLOR` held over a panel without `REFRESH`: `VISIBLE` 1 shows it for its two frames, then the paint wins; a text stays on top |
| digits-out | the value's five places redraw on a change, the panel's background does not, and a paint removes them until the next change |
| digits-in, panel-in | a panel without `REFRESH` inside the window: not there, then there only just after a change |
| text-over | a text moved over a panel without `REFRESH` leaves a trail; with `REFRESH` none |
| top-row(-small) | the view's first row is index 0 in every screenshot, but elements drawn there (panel, text, overlay) show on it |
| pointer | the pointer over the border leaves a trail; inside the window none |
| k-vis, k-pos, k-hold | after a change: shot 1 none (the screen before the frame), shots 2 and 3 the panel, none from 4 on: **two frames** |
| k-dig, k-small-dig | the same for a value's digits, the background never |
| k-first | a panel declared `VISIBLE` shows on the third and fourth frame from `IF_START` |
| start-small | a window made small in `IF_START`: the border holds a full-window view drawn before it |

A survey of all 840 screenshots of every earlier probe: the 3-D window's first row reads index 0 across
its whole width in all 840.

## Rules
- **The screen lasts.** `FRAME_COLOR` not 0 paints the whole screen with that palette entry on each
  frame it holds; 0 paints nothing. Entry 1 of the retail palette is the (4, 4, 4) surround.
- **The 3-D window repaints its own rectangle every frame**, unless `RENDER_MODE` is 0.
- **Its first row reads palette entry 0**: the view starts one row in. A screen element can draw there.
- **A `TEXT`, an overlay, the pointer and a panel with `REFRESH` draw on every frame; a panel without
  `REFRESH` draws a piece on the two frames after that piece changed.** The background, a moved panel
  and each display are separate pieces; a value change redraws its display only.
- **`SCREENSHOT` saves the screen as it stands when the instruction runs**, the last frame drawn.
- **Sampling is at integer pixel positions.** A wall whose top edge projects to row 130.17 starts at
  row 131 and its bottom edge at 349.83 ends at row 349: the row samples its top edge, not its centre.
  The stripes' columns (first column of each `⌊x⌋`) sample each column's right edge. So the picture is
  half a row down and half a column left of a centre-sampled one.

## Confidence
High for the persistence, paint and two-frame rules: each is several carriers and a frame-by-frame
round. Medium for the sampling: measured on a frontal far wall's edges, it takes the plain room from
1742 differing pixels to 470.

## Open Questions
- A far wall's outermost columns where it meets the side walls read one column wider than the
  projection, both ways (26 for 27.25, 613 for 612.75): why the corner, and the ±1 row on oblique edges
  that make up the remaining 470.
- Why two frames (a page flip seems likely); a panel's first frame after `IF_START` is one frame later
  than OpenVirtue's. Whether a `LEVEL` or a `LOAD` repaints anything.
- OpenVirtue keeps the screen in RGB: a `FADE_PAL` changes a palette the original's stored indices then
  read through at once, while a kept border in OpenVirtue keeps its old colour until repainted.

## Implementation
`GlRenderBackend` draws into its own framebuffer and blits it to present; `RuntimeHost.DrawWorld`
sets `HostFrame.PaintColor` from `FRAME_COLOR`, draws the window's backdrop, its top row (`AddTopRow`)
and, in `PanelPieceDraws`, each panel piece on the two frames after its signature changed. `Boot` draws
the first picture before `IF_START`; `FlushPendingScreenshots` moved before the draw. `viewProjection`
moves the picture half a pixel. Tests: `RuntimeHostScreenFrameTests`.
A local measuring tool: `CarrierFrameTests` (`OV_CARRIER_FRAMES`, `OV_CARRIER_OUT`) runs carriers through
the GL host on a fixed clock and writes their screenshots to compare with the original's PCX.
