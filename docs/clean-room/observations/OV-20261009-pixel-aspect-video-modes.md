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
  `VRUN.EXE -NJ TITLE -RUN`, autoexec `exit` afterwards. Every launch is appended to the
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
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| S640x480, ASPECT 1, 16×16 face | w = ?, h = ? px | baseline, expect h/w = 1 if 1:1 in framebuffer pixels |
| S640x480, ASPECT 0.5 | | direction/range of the skill |
| S640x480, ASPECT 2 | | |
| 320x200, ASPECT 1 | | h/w = 1 → no pre-stretch (CRT stretches); ≈ 0.83 → pre-stretched for 1.2:1 pixels |
| X320x240, ASPECT 1 | | square-pixel 4:3 control |
| X320x400, ASPECT 1 | | 0.6:1 pixels; h/w = 1 or ≈ 1.67 |

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
