# OV-20261009-tilt-model-shear-vs-pitch: Is PLAYER_TILT a y-shear or a true camera pitch?
Date: 2026-10-09
Status: proposed
Observer: TBD
Implementer: TBD

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
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| tilt 0 | | baseline: floor-junction row (≈ 332), side-wall vanishing row (eye line); step top off-frame by design |
| tilt 0.5 | edges parallel? step-top row? | shear predicts step top at ≈ 148 (shift 0.5·f ≈ 277 px); pitch predicts converging edges and step top ≈ 171 |
| tilt 1.0 | | shear predicts step top at ≈ 425 (shift ≈ 554 px); pitch ≈ 351 |
| tilt −0.5 | | if positive tilt looks up: floor junction at ≈ 55, side-wall lines give the horizon (≈ −37) |
| tilt −1.0 | | floor junction and step both off-frame under shear; records what remains visible |

## Conclusion
TBD.

## Confidence
—

## Open Questions
- How does the sky background move with tilt (pixel shift = horizon shift?).
- Does `SHOOT` under tilt use the same shear mapping for `SHOOT_Y`?

## Tests To Add Or Update
- If shear: replace camera pitch with a projection-matrix y-offset (shear term) and keep the
  view matrix yaw-only; synthetic test asserting vertical edges remain vertical under tilt.
- If pitch: keep the model, but calibrate the tangent→angle mapping against the measured shift.
