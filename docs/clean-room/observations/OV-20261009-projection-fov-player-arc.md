# OV-20261009-projection-fov-player-arc: Horizontal field of view as a function of PLAYER_ARC
Date: 2026-10-09
Status: proposed
Observer: TBD
Implementer: TBD

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
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| S640x480, `PLAYER_ARC` 0.5 | | |
| S640x480, `PLAYER_ARC` 1.0 | | expect ≈ 60° horizontal per manual |
| S640x480, `PLAYER_ARC` 1.5 | | |
| S640x480, `PLAYER_ARC` 2.0 | | |
| 320x200, `PLAYER_ARC` 1.0 | | width-scaling cross-check |
| X320x400, `PLAYER_ARC` 1.0 | | width-scaling cross-check |

## Conclusion
TBD. Candidate forms to test against the data: f = (W_screen/2) / tan(arc · 30°);
f = (W_screen/2) / arc; f ∝ 1/tan(arc/2). Predicted f at 640 px wide for arc 0.5/1.0/2.0:
form A 1194/554/185; form B 640/320/160; form C (normalised to 554 at arc 1.0) 1185/554/194.
The yellow face (16 steps at D = 48) would span 398/185/62 px under form A versus 213/107/53
under form B, so one run already separates A from B. A and C differ by under 1% at arc 0.5
and 1.5% at arc 1.5; only the arc 2.0 run separates them (185 vs 194 px, 5%), which is why
f is fitted over every stripe edge (sub-pixel, rms ≈ 0.3 px in the self-test) rather than
read off one face.

## Confidence
—

## Open Questions
- Does `ASPECT` (p.102) alter the vertical scale only, as the manual implies?
- Is the vertical pixel scale derived from f alone (pure 2.5D) or adjusted for 320x200's
  1.2 pixel aspect? See OV-20261009-pixel-aspect-video-modes.

## Tests To Add Or Update
- Replace the `PLAYER_ARC`-as-vertical-radians projection with the measured form; add a
  synthetic test projecting a known wall and asserting its screen span for arc 0.5/1.0/2.0.
- Re-derive the hitscan `SHOOT_X/SHOOT_Y` deviation from the same focal length.
