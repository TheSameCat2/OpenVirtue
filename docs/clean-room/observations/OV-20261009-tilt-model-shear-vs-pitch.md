# OV-20261009-tilt-model-shear-vs-pitch: Is PLAYER_TILT a y-shear or a true camera pitch?
Date: 2026-10-09
Status: proposed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
When `PLAYER_TILT` ≠ 0, do vertical wall edges stay vertical on screen (y-shearing: the
horizon line moves by `tan(tilt) · f` pixels) or converge (true perspective pitch)?

## Why It Matters
The app converts `PLAYER_TILT` (manual p.111: "tangent of the player's vertical line of
sight") into a camera pitch (`Atan(tilt)`) and rotates a 3-D view matrix. 2.5D sector engines
of this generation implement look-up/down as a y-shear, and the skill being defined as a
*tangent* is exactly the quantity a shear needs. If the original shears, our tilted frames
show walls leaning that the original never shows, sky/`SKY_OFFS_Y` behavior (p.37: "vertically
the sky texture is neither zoomed nor repeated") differs, and sprite/UI hit testing under tilt
diverges. The GL renderer must pick one model before it is written.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the patched DOS runtime.
- Reference material consulted: A3 manual p.37 (sky vs tilt), p.111 (`PLAYER_TILT`).
- Split-role mode used: not needed.
- Local-only artifacts: `_research/oracle-runs/OV-20261009-tilt-model-shear-vs-pitch/`.
- Relevant local-only file hashes (sha256): retail v1.3 `VRUN.EXE`
  `ddbe4cd0a18adfc754c68d1831f40d9628920facae1a10f4f7fe319b181d491b`; SaintsX 1.1.3
  `vrun-exe.patch` (BSDIFF40) `34531f23ded9b75c8d4410f873cd406f2e32a34db4e2273d6a4bcd7e70aef256`;
  patched copy `VRUN-patched.EXE` `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`
  (applied with the system `bspatch` to a copy; no SaintsX setup script run).
- Confirmation that no proprietary code/assets are committed: synthetic level screenshots only.

## Setup
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
- Room (`tall`): 24 steps wide (y = ±12), far wall at D = 48, ceiling 64, eye at z = 8. The far
  wall is six 4-step segments of alternating red/green (geometric vertical edges, not texture
  stripes). A raised `alcove` region (floor 40, ceiling 64, magenta back wall 16 steps further)
  sits behind the far wall, so the striped face is a 40-step step whose top edge is a horizontal
  geometric line 32 steps above the eye. Under a y-shear of f·tilt (f ≈ 554) that edge sits at
  row cy + shift − f·32/48: off-frame at tilt 0 by design, ≈ 148 at tilt 0.5 and ≈ 425 at
  tilt 1.0, i.e. in frame after every floor/ceiling junction has left it. At tilt 0 the floor
  junction (row cy + f·8/48 ≈ 332) and the side walls' junction lines give the eye line
  directly; negative tilts keep the floor junction in frame. Screenshot name `ovtlt`,
  `VIDEO S640x480`, `PLAYER_ARC` 1.
- Scenario: `SET PLAYER_TILT` 0, 0.5, 1.0, −0.5, −1.0 in `IF_START` (five runs; the manual gives
  no sign for the skill, so the batch is sign-symmetric). The script (a) traces every red/green
  boundary down the far wall and fits a line per edge (parallel ⇒ shear, converging ⇒ pitch),
  (b) reports the step-top row and the floor-junction row, whose displacement between runs is
  the vertical shift to compare against the two models (step top 32 steps above the eye at
  D = 48: shear predicts row = cy + f·tilt − f·32/48, pitch predicts
  row = cy − f·tan(atan(32/48) − atan(tilt))), and (c) the side-wall vanishing row where visible.
- Also note the maximum usable tilt before the renderer clamps or artifacts appear, and the
  sign convention (does positive tilt look up or down).

## Observations
All runs: patched V3.963 under native DOSBox-X, `tall` room (eye z = 8, far wall at D = 48,
six 4-step red/green stripes, step top z = 40 i.e. 32 above the eye, alcove behind it),
`VIDEO S640x480`. Rows are pixel-edge coordinates; "edge angle" is the fitted angle of the
leftmost/rightmost stripe boundary over the rows it is visible in. Predictions use the FOV and
pixel-aspect results (f_v = 0.75·480/(ASPECT·tan(arc/2)) = 659 at arc 1, 231 at arc 2, 329.5
at ASPECT 2) and a pure y-shear of −PLAYER_TILT · SCREEN_HGT pixels. Harness rows are in the
FOV note.

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| tilt 0, arc 1, ASPECT 1 | floor junction row 350; stripes fill rows 1..350 (step top off-frame); eye line from the side walls 240.1; edge angles 0.000° / 0.000° over 349 rows | baseline: cy = 240, floor at 240 + 659·8/48 = 349.8 ✓ |
| tilt 0.5 | floor junction 110 (moved up 240 px); eye line 0.08 (the top edge); stripes rows 1..110; edge angles 0.000° over 109 rows | **positive tilt looks down**; shift −240 px = −tilt · 480 |
| tilt 1.0 | no far wall: the frame is floor only; dump reads TILT=1.000 | predicted floor junction −130: consistent; no clamp at 1.0 in the skill value |
| tilt −0.5 | step top 41 (predicted 240 − 439 + 240 = 40.7); floor off-frame; alcove back wall and ceiling visible above the step; edge angles −0.000° / 0.000° over 439 rows | shift +240 px |
| tilt −1.0 | step top 281 (predicted 280.7); edge angles 0.000° over 199 rows | shift +480 px; still no clamp |
| tilt −0.5, arc 2 (f_v 231) | step top 326 (predicted 86 + 240 = 325.9); eye line from side walls 479.4 (predicted 480); edges 0.000° over 154 rows | a shift proportional to f_v (0.5 · 231 · 0.365 = 42 px) would have put the step at 128: rejected |
| tilt −0.5, ASPECT 2 (f_v 329.5) | step top 261 (predicted 22 + 240 = 260.3); eye line 481.2; edges 0.000° over 219 rows | shift is independent of ASPECT too |
| all seven runs | every stripe boundary is a perfect screen column (fitted angle 0.000°, difference between left and right edges 0.000°); the floor and the alcove ceiling converge in perspective as planes do | a true pitch of atan(0.5) would have shown ≈ 8.5° between the outer edges (self-test of the script) |

## Conclusion
Confirmed for patched V3.963: `PLAYER_TILT` is a **y-shear**, not a camera pitch. Vertical
world edges stay vertical on screen at every tilt, and the whole projected scene (eye line,
floor and ceiling junctions, far-wall features) moves by

    Δrow = −PLAYER_TILT · SCREEN_HGT

pixels: the eye line sits at row SCREEN_HGT/2 − PLAYER_TILT · SCREEN_HGT, so tilt 0.5 puts it
on the top edge and tilt −0.5 on the bottom edge, positive tilt looks down. The shift is a
screen-space quantity: it does not change with `PLAYER_ARC` or `ASPECT` (arc 2 and ASPECT 2
runs). In view-space terms the eye-line offset in tangent units is
PLAYER_TILT · SCREEN_HGT / f_v = (4/3) · ASPECT · tan(PLAYER_ARC/2) · PLAYER_TILT, so the skill
is "the tangent" only up to that factor (0.728 at the defaults). No clamping was observed at
|tilt| = 1.

## Confidence
high

Seven runs, all stripe edges vertical to the fitting precision, and every feature row within
1 px of the shear prediction including the two runs that vary f_v. The sign is unambiguous.

## Open Questions
- Behaviour beyond |tilt| = 1 (clamp, wrap, artifacts) was not probed; at ±1 the eye line is
  already one full screen height away.
- How the sky bitmap moves with tilt (expected: by the same Δrow, since p.37 says it is
  neither zoomed nor repeated vertically) was not probed — no SKY in the fixture.
- Whether `SHOOT_Y` under tilt uses the same shear (a ray through screen row r has vertical
  slope (cy − Δrow − r)/f_v) was not probed.
- Whether Δrow uses `SCREEN_HGT` or the mode height when the 3-D window is smaller than the
  mode was not probed; written as `SCREEN_HGT` by analogy with f_v.

## Tests To Add Or Update
- `ProjectionModel`: eye-line row for tilt 0 / 0.5 / −0.5 at 480 rows → 240 / 0 / 480;
  the shear offset is unchanged by arc and ASPECT; projecting the two ends of a vertical
  world edge under tilt 0.5 yields the same screen x.
- Replace the camera pitch (`Atan(PLAYER_TILT)` view rotation) with a projection-matrix
  y-offset; keep the view matrix yaw-only; synthetic test asserting vertical edges stay
  vertical under tilt.
- Hitscan: derive the vertical slope of a `SHOOT_Y` ray from the same offset.
