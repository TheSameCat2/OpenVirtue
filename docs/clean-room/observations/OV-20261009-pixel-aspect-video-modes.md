# OV-20261009-pixel-aspect-video-modes: Pixel aspect and vertical scale per VIDEO mode
Date: 2026-10-09
Status: proposed
Observer: TBD
Implementer: TBD

## Question
For each `VIDEO` mode the retail worlds use, what is the ratio of a wall's rendered pixel
height to its pixel width for a square (W = H in steps) wall face viewed head-on? I.e. does
the engine pre-stretch geometry vertically for 320x200's 1.2:1 pixels (as Doom does), or
render 1:1 in framebuffer pixels and leave the CRT to stretch it?

## Why It Matters
`VirtualDisplayLayout.Create` scales the virtual grid **uniformly** into the window, so a
320x200 world is presented at 16:10 with square pixels. On original hardware 320x200 filled a
4:3 display; every pixel was 1.2× taller than wide. Depending on the answer above, either the
3-D geometry or the whole frame (geometry + HUD) must be presented 20% taller than today.
The `ASPECT` skill (manual p.102, default 1 = "1:1") is not implemented at all. UI layout,
mouse hit testing and sprite proportions all depend on this.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the patched DOS runtime; `WVRUN.EXE` under Wine/Windows as a
  secondary check for the DirectX build's window shape.
- Reference material consulted: A3 manual p.29 (`VIDEO` modes), p.102 (`SCREEN_*`, `ASPECT`).
- Split-role mode used: not needed.
- Local-only artifacts: `_research/oracle-runs/OV-20261009-pixel-aspect-video-modes/`.
- Confirmation that no proprietary code/assets are committed: synthetic level screenshots only.

## Setup
- First, record (sanitized: mode names only) which `VIDEO` mode each retail world declares:
  `ovtool level info` on the six archives. Note that SaintsX patches in 800x600, so the
  unpatched WDL is the reference.

  Recorded 2026-10-09 from the unpatched retail v1.3 archives (`ovtool level info` now prints
  the flattened `VIDEO` mode; alternatives come from a keyword-only scan of each main script's
  `IFDEF`/`DEFINE` header, no script content copied):

  | Archive | Active `VIDEO` (no `-d` define) | Alternatives selectable at the command line |
  |---|---|---|
  | apathy | `S640x480` | `-d LORES` → `320x200`; `-d MEDRES` → `X320x400`; `-d HIRES` → `S640x480` |
  | HEART | `S640x480` | same three-way `HIRES`/`LORES`/`MEDRES` header |
  | LEGALISM | `S640x480` | same |
  | NEWAGE | `S640x480` | same |
  | START | `S640x480` | none (single unconditional `VIDEO`) |
  | TITLE | `S640x480` | none (single unconditional `VIDEO`) |

  Consequences for this probe: the shipped default is 640x480, a 4:3 mode with square pixels,
  so the current uniform letterbox is already correct for the default path. The 320x200 /
  X320x400 question matters for the `-d LORES` / `-d MEDRES` launch paths and for engine
  generality (both modes are listed in the manual, p.29). The retail scripts never reference
  `ASPECT`; each gameplay world references `PLAYER_ARC` 3 times and `PLAYER_TILT` 17–24
  times, so the FOV and tilt probes carry the parity weight.
- Synthetic level: a wall face exactly 64 × 64 steps with a 64×64 checker texture at
  `SCALE_XY 1,1`, viewed head-on, `PLAYER_TILT = 0`, `ASPECT = 1`.
- Screenshot in DOSBox-X with `aspect=false` (raw pixels); measure pixel width vs height of
  the face. Repeat with `ASPECT = 0.5` and `2` to confirm the skill's direction and range.
- Repeat in `X320x240` and (if the runtime allows) `S640x480`.

## Observations
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| 320x200, ASPECT 1, 64×64 face | w = ?, h = ? px | ratio h/w tells pre-stretch vs none |
| 320x200, ASPECT 2 | | |
| X320x240, ASPECT 1 | | |

## Conclusion
TBD.

## Confidence
—

## Open Questions
- Does the Windows runtime (`WVRUN.EXE`) stretch 320x200 to a 4:3 window/fullscreen mode?
- Do panels/text (`POS_X/Y` in virtual pixels) stretch with the frame? (They must — they are in
  the same framebuffer — but confirm for the DirectX build.)

## Tests To Add Or Update
- `VirtualDisplayLayout.Create` gains a pixel-aspect parameter per `VIDEO` mode; letterbox to
  the display aspect (4:3 for 320x200/X320x400), not to the virtual grid's aspect.
- Implement `ASPECT` in the projection; synthetic test for the head-on square face ratio.
