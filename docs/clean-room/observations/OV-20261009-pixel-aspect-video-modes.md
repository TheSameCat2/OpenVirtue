# OV-20261009-pixel-aspect-video-modes: Pixel aspect and vertical scale per VIDEO mode
Date: 2026-10-09
Status: confirmed (implemented 2026-10-09 in `ProjectionModel`, `VirtualDisplayLayout`, `LevelVideoMode.DisplayAspect`)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

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
- Relevant local-only file hashes (sha256): retail v1.3 `VRUN.EXE`
  `ddbe4cd0a18adfc754c68d1831f40d9628920facae1a10f4f7fe319b181d491b`; SaintsX 1.1.3
  `vrun-exe.patch` (BSDIFF40) `34531f23ded9b75c8d4410f873cd406f2e32a34db4e2273d6a4bcd7e70aef256`;
  patched copy `VRUN-patched.EXE` `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`
  (applied with the system `bspatch` to a copy; no SaintsX setup script run).
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
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `autolock=false`; carrier `work/` mounted as C:, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT` (after `dir /b > OVDIR.TXT`), autoexec `exit` afterwards. Every launch is appended to the
  local-only `launches.md` by the generated `run.sh`.
- Fixture generator: `ovtool oracle prepare <probe> --runtime <VRUN-patched.EXE>` writes one
  carrier per parameter value (generated WDL, WMP, flat-color 64x64 PCX textures at SCALE 4 px/step,
  a palette PCX with no shading RANGEs so every pixel is a flat palette index, DITHER 0).
  Player at the origin facing +X (`PLAYER_START 0 0 0 0`); `IF_START` raises the eye to z = 8
  (`SET PLAYER_SIZE, 8` and `SET PLAYER_Z, 8`, both documented settable skills, p.109/p.114) so
  the 16-step face is vertically centered; one rectangular region closed by a solid border.
  `IF_START` then SETs the probe skills, SETs MOTION_BLUR 0 and RENDER_MODE 2 (p.103: required
  after a direct player displacement), WAITTs 16 ticks, PRINTFILEs the live skill values to
  `OVPRB0.TXT` (so the actual eye height is recorded), grabs
  `SCREENSHOT` into `<name>0.PCX`, WAITTs 4 ticks and EXITs. Measurement:
  `tools/oracle/measure_projection.py` (deterministic; `--selftest` recovers the constants of a
  pinhole re-render of the same room within 2%).
- Room (`face`, shared with the FOV probe): the centered yellow far-wall face is 16 steps wide
  and the room is 16 steps high (floor 0, ceiling 16), so the face is a square 16 × 16 steps at
  D = 48 viewed head-on, `PLAYER_TILT = 0`, `PLAYER_ARC = 1`. The script measures the face's
  pixel width (left/right edges on the far-wall row) and pixel height (ceiling-junction row to
  floor-junction row at the face's center column). Screenshot name `ovasp`.
- Variants: `S640x480` at `ASPECT` 1 (no SET), 0.5 and 2 (`SET ASPECT, x` in `IF_START`);
  `320x200`, `X320x240`, `X320x400` at `ASPECT` 1. Manual ambiguity, recorded rather than
  guessed: p.102 lists `ASPECT` among the predefined skills ("height-to-width ratio of the
  rendered scene … default 1") while the p.132 split-screen example writes `ASPECT 0.5;` as a
  top-level statement; the SET form is tried first and a top-level `ASPECT x;` fallback is the
  one change made if the compiler rejects SET.

## Observations
All runs: patched V3.963 under native DOSBox-X, `face` room (eye z = 8, far wall at D = 48,
yellow face 16 wide × 16 high), `PLAYER_ARC` 1. w = face width on the far-wall row, h = face
height (ceiling junction to floor junction, median of five columns), both in framebuffer
pixels; f_h/f_v = w·48/16 and h·48/16. Harness rows are in the FOV note.

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| S640x480, ASPECT 1 (no SET), 16×16 face | w = 195, h = 219 px; h/w = 1.123; f_h 586, f_v 657; face rows 131..350, horizon 240.5 | not 1:1 even with square pixels |
| S640x480, `SET ASPECT, 0.5` | w = 195, h = 441; h/w = 2.262; f_v 1323 (×2.01); rows 20..461 | ASPECT 0.5 doubles the vertical scale; horizontal unchanged. The dump reports ASPECT=0.500, so SET on the skill is accepted and takes effect |
| S640x480, `SET ASPECT, 2` | w = 195, h = 109; h/w = 0.559; f_v 327 (×0.498); rows 186..295 | ASPECT 2 halves the vertical scale |
| 320x200, ASPECT 1 | w = 97, h = 91; h/w = 0.938; f_h 292.5, f_v 273; rows 55..146, horizon 100.5 | lower than 1: no pre-stretch for 1.2:1 pixels |
| X320x240, ASPECT 1 | w = 97, h = 109; h/w = 1.124; f_v 327; rows 66..175, horizon 120.5 | same ratio as 640x480 (both 4:3 square-pixel modes) |
| X320x400, ASPECT 1 | w = 97, h = 183; h/w = 1.887; f_v 549; rows 109..292, horizon 200.5 | |
| FOV runs at 640x480, ASPECT 1 (see FOV note) | f_v/f_h = 1.128 / 1.121 / 1.127 / 1.125 at arc 0.5 / 1.0 / 1.5 / 2.0 | the ratio does not depend on the arc |
| `SET ASPECT` syntax | accepted by the compiler at 640x480; `PRINT_VALUE ASPECT` prints the set value | the top-level `ASPECT n;` form of p.132 was not needed and not tried |

Model check (f_v = 0.75 · SCREEN_HGT / (ASPECT · tan(arc/2)); ratio = 1.5 · (H/W) / ASPECT):

| Mode, ASPECT | measured f_v | model f_v | measured f_v/f_h | model ratio | display pixel aspect (4:3 CRT) | on-screen h/w of a square |
|---|---|---|---|---|---|---|
| 640x480, 1 | 657 | 659.0 | 1.121 | 1.125 | 1.0 | 1.12 |
| 640x480, 0.5 | 1323 | 1318 | 2.258 | 2.25 | 1.0 | 2.25 |
| 640x480, 2 | 327 | 329.5 | 0.558 | 0.5625 | 1.0 | 0.56 |
| 320x200, 1 | 273 | 274.6 | 0.933 | 0.9375 | 1.2 | 1.12 |
| X320x240, 1 | 327 | 329.5 | 1.118 | 1.125 | 1.0 | 1.12 |
| X320x400, 1 | 549 | 549.1 | 1.877 | 1.875 | 0.6 | 1.12 |

## Conclusion
Confirmed for patched V3.963:

    f_v = (3/4) · SCREEN_HGT / (ASPECT · tan(PLAYER_ARC / 2))        f_h = (SCREEN_WIDTH / 2) / tan(PLAYER_ARC / 2)

so a square face's height-to-width ratio in framebuffer pixels is 1.5 · (SCREEN_HGT /
SCREEN_WIDTH) / ASPECT. The engine does **not** pre-stretch for a mode's pixel aspect; it
scales the vertical axis to the window height only. Because the VGA modes' pixel aspects
(1.0, 1.2, 1.0, 0.6) are exactly the 4:3-display corrections of their grids, every mode shows
the same picture on a 4:3 display: a geometric square appears 12.5 % taller than wide at
ASPECT 1 (the vertical half-tangent is 2/3 of the horizontal one, where 3/4 would be
geometrically true). The `ASPECT` skill divides the vertical scale (0.5 doubles the height,
2 halves it), consistent with the manual's split-screen example that sets 0.5 for a
half-height window.

Presentation rule for OpenVirtue: present every VGA-mode framebuffer at the display aspect
4:3 (letterbox to 4:3, not to the virtual grid's own aspect: 320x200 and X320x400 must be
stretched to 4:3, 640x480 and X320x240 are already 4:3), and build the 3-D projection from
f_h and f_v above rather than from a square-pixel vertical FOV. For the six retail worlds
(all `S640x480`, square pixels) the current uniform letterbox is already correct and only the
projection changes: f_v/f_h must be 1.125, not 1.0. The same formulas hold for the
`-d LORES`/`-d MEDRES` 320x200 / X320x400 paths once the frame is shown at 4:3.

## Confidence
high

Three VIDEO modes and three ASPECT values; every measured f_v is within 1 % of the model and
every h/w within 0.005. The only extrapolation is the 4:3-display claim for 320x200 and
X320x400, which follows from VGA hardware, not from the probe.

## Open Questions
- The Windows runtime (`WVRUN.EXE`, DirectX): whether its window or full-screen mode shows
  320x200/X320x400 at 4:3 was not probed (Wine, secondary oracle per `_research/README.md`).
- Panels/text (`POS_X/Y` in virtual pixels) share the framebuffer and therefore the 4:3
  presentation; not separately verified.
- Whether `ASPECT` also scales the sky, overlays or the automap was not probed.
- The ratio 2/3 (measured 0.665–0.669) is taken as exact.

## Tests To Add Or Update
Added:
- `VirtualDisplayLayoutTests.Create_320x200WithFourByThreeDisplay_StretchesRowsByOnePointTwo`
  (scale x 4, y 4.8 in a 1280x960 client), `Create_X320x400WithFourByThreeDisplay_SquashesRowsToZeroPointSix`,
  `Create_TallClientWithFourByThreeDisplay_LetterboxesToDisplayAspect`,
  `Create_SquarePixelMode_IsUnchangedByDisplayAspect`.
- `ProjectionModelTests.FocalLengthY_VideoModes_IsThreeQuartersHeightOverTangent` (659.0 /
  274.6 / 329.5 / 549.1 px), `FocalLengthY_AspectSkill_DividesVerticalScale` (1318 / 329.5),
  `Project_SixteenStepSquareFaceAt48_Is195By220PixelsAt640x480`,
  `Project_SquareFaceAt320x200_IsShorterThanWideInFramebufferPixels`.
- `ASPECT` is a runtime skill (default 1, range 0.1..10) consumed by the projection and the
  hitscan: `WdlRuntimeTests.Runtime_ShootInstruction_AspectTwoWidensVerticalAim`.
