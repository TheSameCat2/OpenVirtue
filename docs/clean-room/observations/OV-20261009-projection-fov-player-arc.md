# OV-20261009-projection-fov-player-arc: Horizontal field of view as a function of PLAYER_ARC
Date: 2026-10-09
Status: confirmed (implemented 2026-10-09 in `OpenVirtue.Engine.Rendering.ProjectionModel`)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
What horizontal field of view does the original renderer use for a given `PLAYER_ARC`, and
what is the functional form (focal length in pixels vs. `PLAYER_ARC`, at `ASPECT = 1`)?

## Why It Matters
The current app (`Camera.Projection`) passes `PLAYER_ARC` straight into
`CreatePerspectiveFieldOfView` as a **vertical** FOV **in radians** (1.0 → 57.3° vertical, so
~85° horizontal at 16:10). The 1998 manual (p.111) defines `PLAYER_ARC` as the eye's "focal
length range … (0.2..2.0)" where the default 1.0 is "approximately the field of vision of the
human eye (ca. 60°)" — a horizontal field, and not literally radians. The hitscan projection
(`SHOOT_X/SHOOT_Y`) shares the same constants. This is the most likely cause of the
"proportions out of whack" seen in the last Windows demo, and the GL renderer (ADR-0008)
must not inherit it. Parity of every sprite size, wall height and distance impression hangs on it.

## Clean-Room Boundary
- Oracle used: black-box DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), or the
  Skaphander 1995 runtime as a cross-check.
- Reference material consulted: 1998 A3 manual p.102 (`SCREEN_*`, `ASPECT`), p.111 (`PLAYER_ARC`,
  `PLAYER_TILT`); no source-level material.
- Split-role mode used: not needed (black-box geometric measurement).
- Local-only artifacts: `_research/oracle-runs/OV-20261009-projection-fov-player-arc/`.
- Relevant local-only file hashes (sha256): retail v1.3 `VRUN.EXE`
  `ddbe4cd0a18adfc754c68d1831f40d9628920facae1a10f4f7fe319b181d491b`; SaintsX 1.1.3
  `vrun-exe.patch` (BSDIFF40) `34531f23ded9b75c8d4410f873cd406f2e32a34db4e2273d6a4bcd7e70aef256`;
  patched copy `VRUN-patched.EXE` `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`
  (applied with the system `bspatch` to a copy; no SaintsX setup script run).
- Confirmation that no proprietary code/assets are committed: screenshots of a synthetic
  level only; retail screenshots stay local.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963 (patched copy of the retail v1.3 binary).
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
- Primary mode is `VIDEO S640x480` because every retail world declares it (see
  OV-20261009-pixel-aspect-video-modes); one `320x200` and one `X320x400` run at `PLAYER_ARC`
  1.0 cross-check whether f scales with the window width.
- Room (`face`): 48 steps wide (y = ±24), far wall at D = 48, ceiling 16. The far wall is split
  into 4-step segments of alternating red/green with a centered yellow face 16 steps wide
  (y = ±8), so every segment boundary is a geometric edge at a known y. Screenshot name `ovfov`.
- Scenario: one run per `PLAYER_ARC` = 0.5, 1.0, 1.5, 2.0 (`SET PLAYER_ARC` in `IF_START`,
  `SET PLAYER_TILT, 0`). Along the far-wall row the script lists every stripe edge, maps it to
  its world y and fits x = cx + (f/D)·y, giving f in pixels; it also reports f from the yellow
  face alone (f = S·D/W) and from the median stripe spacing. Horizontal FOV = 2·atan((W_screen/2)/f).

## Observations
All runs: SaintsX-patched V3.963 under native DOSBox-X, synthetic `face` room, player at the
origin facing +X, eye z = 8, far wall at D = 48, stripes 4 steps, yellow face 16 × 16 steps.
Pixel numbers are from `tools/oracle/measure_projection.py` (`measurements.csv` beside each
run); edges are pixel boundaries. "fit" = least squares over every stripe edge mapped to its
world y (10 edges, residual rms ≤ 0.4 px); "face" = yellow face width only; "spacing" = median
stripe spacing.

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| 2026-10-09 first launch, `S640x480-arc-1` | DOSBox-X ran 9 s and exited 0; no marker files, no screenshot (`run.sh` rc 3). | The DOS screen is not in the DOSBox-X log, so the cause was invisible. |
| 2026-10-09 diagnostic relaunch, same carrier, autoexec `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT` | Console file shows the V3.963 (1998-05-30) banner, "World definition TITLE.WDL opened", then `Error(90): Filename length` on `PRINT_STRING "PLAYER_ANGLE=";` and `PRINT_STRING "SCREEN_WIDTH=";`, "2 Errors … engine shutdown". | Harness fact: this compiler rejects inline string literals longer than 12 characters. Console redirection works and is now part of every launcher (`OVCON.TXT`), with `dir /b > OVDIR.TXT` recording what DOS saw. |
| 2026-10-09 relaunch with ≤ 6-character labels | `OVSTA0.TXT` = START, `OVPRB0.TXT` skill dump, `OVEND0.TXT` = END and the screenshot (640×480, 8-bit, PCX version byte 5, 768-byte palette trailer) all present; console shows "Initializing nexus", no errors. | `SCREENSHOT "ovfov",0` writes `OVFOV0.PCX` (one-digit suffix, like PRINTFILE in OV-20260701). The dump reads ARC=1.000 TILT=0.000 PX=0 PY=0 PZ=8.000 ANG=0 PSIZE=8.000 FLOOR=0 CEIL=16.000 SX=0 SY=0 SW=640 SH=480 BLUR=0 after the 16-tick wait, so `SET PLAYER_SIZE`/`SET PLAYER_Z` held. |
| every screenshot, all three probes | Row 0 of the frame is palette index 0 (black) across the full width; rows 1..479 hold the scene. Column 0 and 639 are scene pixels. | Measured centers come out at cx = 319.5, cy = 240.5 in pixel-edge coordinates (±0.5 px); whether the 3-D window is rows 1..479 or the renderer uses a half-pixel offset is not resolved by these probes. |
| S640x480, `PLAYER_ARC` 0.5 | f_h fit 1253.1 px (rms 0.29), face 417 px → 1251, spacing → 1260; only 4 edges in frame (blue side walls off-screen) | candidates at W/2 = 320: (W/2)/tan(arc/2) = 1253.2; (W/2)/tan(arc·30°) = 1194; (W/2)/arc = 640 |
| S640x480, `PLAYER_ARC` 1.0 | f_h fit 586.0 (rms 0.39), face 195 px → 585, spacing → 588; 10 edges | candidates: 585.8; 554.3; 320. Horizontal FOV 2·atan(320/586) = 57.3°, not 60° |
| S640x480, `PLAYER_ARC` 1.5 | f_h fit 343.5 (rms 0.29), face 115 px → 345, spacing → 342 | candidates: 343.5; 320.0; 213.3 |
| S640x480, `PLAYER_ARC` 2.0 | f_h fit 205.3 (rms 0.17), face 69 px → 207, spacing → 204 | candidates: 205.5; 184.8; 160.0 |
| 320x200, `PLAYER_ARC` 1.0 | f_h fit 292.5 (rms 0.29), face 97 px → 291 | (W/2)/tan(0.5) at W/2 = 160 is 292.9: f_h follows SCREEN_WIDTH only |
| X320x400, `PLAYER_ARC` 1.0 | f_h fit 292.5 (rms 0.29), face 97 px | identical to 320x200: the window height does not enter f_h |
| vertical scale, same runs (face height h over 16 steps, f_v = h·48/16) | 640x480: arc 0.5 h 471 → f_v 1413; arc 1.0 h 219 → 657; arc 1.5 h 129 → 387; arc 2.0 h 77 → 231. 320x200: h 91 → 273. X320x400: h 183 → 549 | f_v/f_h = 1.128, 1.121, 1.127, 1.125 (640x480), 0.933 (320x200), 1.877 (320x400); tan(v/2)/tan(h/2) = 0.665–0.669 at every arc. See OV-20261009-pixel-aspect-video-modes |
| eye line, every run | horizon row from the side-wall junction lines 240.4–240.5 (640x480), 100.5 (320x200), 200.5 (320x400); the face is symmetric about it (eye at 8 of 16) | cy = SCREEN_HGT/2 (+0.5 px) |
| vertical edges, every run | fitted angle of the leftmost/rightmost stripe edge 0.000° | columns are exact: a column renderer |

Candidate fit summary (f_h in px, measured vs. form):

| `PLAYER_ARC` | measured (fit) | C: (W/2)/tan(arc/2) | A: (W/2)/tan(arc·30°) | B: (W/2)/arc |
|---|---|---|---|---|
| 0.5 | 1253.1 | 1253.2 (−0.01 %) | 1194.3 (−4.7 %) | 640 (−49 %) |
| 1.0 | 586.0 | 585.8 (−0.03 %) | 554.3 (−5.4 %) | 320 (−45 %) |
| 1.5 | 343.5 | 343.5 (0.00 %) | 320.0 (−6.8 %) | 213.3 (−38 %) |
| 2.0 | 205.3 | 205.5 (+0.1 %) | 184.8 (−10 %) | 160 (−22 %) |

## Conclusion
Confirmed for patched V3.963: the horizontal projection is a pinhole with focal length

    f_h = (SCREEN_WIDTH / 2) / tan(PLAYER_ARC / 2)

i.e. **the horizontal field of view is exactly `PLAYER_ARC` radians** (57.30° at the default
1.0; the manual's "ca. 60°" is an approximation). The form holds at all four arcs to within
0.1 % and is independent of the window height and of the VIDEO mode. `PLAYER_ARC` is neither a
vertical FOV nor "radians" in the sense the current app uses it (vertical, 1.0 → 57° vertical).

The vertical scale measured in the same runs is not f_h: f_v = (3/4) · SCREEN_HGT / tan(PLAYER_ARC/2)
(the vertical half-tangent is 2/3 of the horizontal one whatever the window shape; the
pixel-aspect note covers this and the `ASPECT` skill).

## Confidence
high

Four arcs, three VIDEO modes, every estimator (edge fit, face width, stripe spacing) agrees
within 1 px, residuals below 0.4 px, and the competing forms are off by 5–50 %. The state
dump proves the skills had the intended values when the frame was grabbed.

## Open Questions
- Half-pixel convention: the measured center is 319.5/240.5 in pixel-edge coordinates with
  row 0 always black. Either the window is rows 1..479 or the renderer centers on pixel
  (319, 240); the difference is 0.5 px and does not affect the focal length.
- The ratio tan(v/2)/tan(h/2) measured 0.665–0.669; 2/3 is assumed exact (see the
  pixel-aspect note).
- `SHOOT_X`/`SHOOT_Y` are documented as deviations "from the center of the 3-D window within
  0..1"; whether they map through the same tangents (x → atan(SHOOT_X · tan(arc/2))) was not
  probed. OpenVirtue derives hitscan from the same model until a SHOOT probe says otherwise.
- A 3-D window smaller than the mode (`SCREEN_WIDTH`/`SCREEN_HGT` below the maximum) was not
  probed; the formulas are written in terms of the window size as the manual's split-screen
  example (p.132) implies.

## Tests To Add Or Update
Added in `tests/OpenVirtue.Engine.Tests/Rendering/ProjectionModelTests.cs`:
- `FocalLengthX_Width640_IsHalfWidthOverTangentOfHalfArc` (arc 0.5/1.0/1.5/2.0 → 1253.2 /
  585.8 / 343.5 / 205.5 px), `FocalLengthX_Width320_FollowsWindowWidthOnly` (292.9 px),
  `HorizontalFieldOfView_DefaultArc_IsOneRadian`.
- `Project_SixteenStepSquareFaceAt48_Is195By220PixelsAt640x480` (the yellow face).
- `ShootRay_Center_FollowsTiltAndEdgesFollowHalfTangents`; the runtime hitscan tests
  `Runtime_ShootInstruction_ShootYUsesProjectedVerticalAim` and
  `Runtime_ShootInstruction_PositiveShootXAimsToScreenRight` now derive from the same model.

## Implementation Notes
Implemented 2026-10-09: `OpenVirtue.Engine.Rendering.ProjectionModel` holds f_h, f_v (next
note) and the tilt shear (OV-20261009-tilt-model-shear-vs-pitch); `CreateProjectionMatrix`
carries the shear in M32, `Camera.View` is yaw-only and `LevelWindow.CurrentProjection()`
feeds the D3D11 renderer, the sprite click test and (through `WdlRuntime`) the hitscan. The
GL renderer under ADR-0008 must consume the same type.
