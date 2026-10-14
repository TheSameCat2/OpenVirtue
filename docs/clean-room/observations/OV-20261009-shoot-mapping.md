# OV-20261009-shoot-mapping: Do SHOOT_X/SHOOT_Y aim through the renderer's projection, and what do HIT_DIST/HIT_X/HIT_Y report?
Date: 2026-10-09 (runs 2026-10-10)
Status: confirmed (implemented 2026-10-10: `ProjectionModel.ShootRay` sign, `WdlRuntime` hit depth, `MeshBuilder` wall u, engine commit `2a817da`)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
For a `SHOOT` fired by the player at the documented defaults (`PLAYER_ARC` 1, `ASPECT` 1,
`VIDEO S640x480`):

1. **Lateral mapping and sign.** Is the ray's lateral deviation for `SHOOT_X = s` the one
   through the screen column `s · SCREEN_WIDTH/2` from the center, i.e. a lateral tangent of
   `s · tan(PLAYER_ARC/2)` (what OpenVirtue assumed), or a linear angle `s · PLAYER_ARC/2`?
   Does positive `SHOOT_X` aim to the original's screen right (map −y when facing +x,
   OV-20261009-tilt-model-shear-vs-pitch)?
2. **Vertical mapping and sign.** Does `SHOOT_Y = s` give a vertical slope of magnitude
   `s · (2/3) · ASPECT · tan(PLAYER_ARC/2)` (the vertical half-tangent of
   OV-20261009-pixel-aspect-video-modes), and which way is positive?
3. **Tilt.** With `SHOOT_Y = 0` and `PLAYER_TILT ≠ 0`, does the ray follow the y-shear
   (slope `−PLAYER_TILT · SCREEN_HGT / f_v`), or does `SHOOT` ignore the tilt?
4. **Hit skills.** Is `HIT_DIST` the distance along the ray or the depth along the line of
   sight? Are `HIT_X`/`HIT_Y` texels from the bitmap's upper-left corner (manual p.90) with the
   bitmap's left edge on the screen left? Are they valid inside the target's `IF_HIT` action,
   which the manual does not say (p.90 only: "after the SHOOT instruction")? Does `RESULT`
   follow `SHOOT_FAC · (1 − HIT_DIST / SHOOT_RANGE)` (p.90)?
5. **By-products.** Which half of a thing bitmap is drawn on the screen left (sprite
   mirroring), and which half of a wall bitmap sits toward the wall's first vertex (wall
   texture orientation, the open question ADR-0009 left unprobed)?

## Why It Matters
`WdlRuntime.ShootFromPlayer` builds the ray from `ProjectionModel.ShootRay`, i.e. it assumed
the hitscan shares the renderer's measured projection and used OpenVirtue's own sign
conventions (positive `SHOOT_X` clockwise = screen right, positive `SHOOT_Y` down). The
four hitscan tests encoded that assumption, not an observation. The retail weapon actions
fire `SHOOT`; whether a shot lands where the crosshair points, and where a bullet-hole
`ATTACH` is placed from `HIT_X`/`HIT_Y`, depends on these answers. The sprite and wall
texture orientation decides whether the renderer must flip `u` (ADR-0009 deliberately did
not).

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output markers
  plus one screenshot per variant.
- Reference material consulted: 1998 A3 manual p.90–91 (`SHOOT`, `HIT_X`/`HIT_Y`), p.112–113
  (`SHOOT_X`/`SHOOT_Y`, `HIT_DIST`, `RESULT`), p.70 (`THING … IF_HIT`), p.72 (`IMMATERIAL`),
  p.37 (bitmap sizes up to 1024 px), p.39 (`SCALE_X/Y` in pixels per step), p.88–89
  (`PRINTFILE`, `PRINT_VALUE`, `SCREENSHOT`). No source-level material.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261009-shoot-mapping/` (generated
  carriers, raw `OV*.TXT`/`OV*.PCX` outputs, `launches.md`, `measurements.csv`).
- Relevant local-only file hashes (sha256): retail v1.3 `VRUN.EXE`
  `ddbe4cd0a18adfc754c68d1831f40d9628920facae1a10f4f7fe319b181d491b`; SaintsX 1.1.3
  `vrun-exe.patch` (BSDIFF40) `34531f23ded9b75c8d4410f873cd406f2e32a34db4e2273d6a4bcd7e70aef256`;
  patched copy `VRUN-patched.EXE` `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`
  (applied with the system `bspatch` to a copy; no SaintsX setup script run).
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing
  under `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`,
  `aspect=false`, `output=surface`, `autolock=false`; carrier `work/` mounted as C:, launched
  as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`, autoexec `exit` afterwards (the launcher shared
  with the three projection probes; every launch appended to the local-only `launches.md`).
- Fixture generator: `ovtool oracle prepare shoot-mapping --runtime <VRUN-patched.EXE>`. The
  `face` room of the FOV/aspect probes (48 wide, far wall at x = 48, ceiling 16, 4-step
  red/green stripes, centered 16-step yellow face; player at the origin facing +x,
  `PLAYER_START 0 0 0 0`; eye raised to z = 8 by `SET PLAYER_SIZE, 8; SET PLAYER_Z, 8;`),
  plus three THINGs **inside** the room at x = 44: `tgt_left` at map y = +12 (the original's
  screen left), `tgt_center` at 0, `tgt_right` at −12. Each is 4 steps wide × 16 tall: a
  64 × 256 px bitmap at `SCALE_X 16; SCALE_Y 16;` (left half cyan, right half orange), so
  `HIT_X`/`HIT_Y` resolve 1/16 step and the eye line is the bitmap's row 128. The yellow face
  bitmap is left-half yellow / right-half light blue; its wall runs from the vertex at
  y = −8 (first) to the vertex at y = +8 (second), with the room on the wall's left. Palette
  with no `RANGE` (shading off), `DITHER 0`, `NEXUS 14`.
- `IF_START` (`probe_start`): `SET PLAYER_ARC/PLAYER_TILT/PLAYER_SIZE/PLAYER_Z`, `SET SHOOT_X`,
  `SET SHOOT_Y`, `MOTION_BLUR 0`, `RENDER_MODE 2`, `WAITT 16`, the skill dump to `OVPRB0.TXT`
  (the projection probes' labels plus `SHX`, `SHY`, `SHRNG`, `SHFAC`, `TCK`), then
  `PRINTFILE "ovsho"`, `PRINT_STRING "PRE"`, **`SHOOT;`**, `PRINTFILE "ovsho"` again and the
  five `HOUT_*` values (`HIT_DIST`, `HIT_X`, `HIT_Y`, `RESULT`, `TICKS`), `WAITT 4`,
  `SCREENSHOT "ovsht", 0`, `OVEND0.TXT`, `EXIT`. Each target's `IF_HIT` action selects
  `OVHIT0.TXT`, writes its letter (`L`/`C`/`R`) and the same five skills as `HIN_*`. Labels
  stay ≤ 12 characters (compiler limit, FOV note).
- Variants (`S640x480`, arc 1, aspect 1, `SHOOT_RANGE` 500, `SHOOT_FAC` 1) and the predicted
  hit under the shared-projection model as implemented before the probe (ray from the eye
  (0, 0, 8) along +x; lateral tangent `SHOOT_X · tan(0.5)` toward map −y; slope
  `−SHOOT_Y · (2/3) · tan(0.5) − PLAYER_TILT · 480 / 659`; target plane x = 44; texel from the
  target's upper-left = (+y edge, z = 16) at 16 px/step), next to the competing readings
  `geometry.json` carries:

  | variant | SHOOT_X | SHOOT_Y | TILT | predicted target, map (y, z) | predicted texel (x, y) | competing readings |
  |---|---|---|---|---|---|---|
  | `center` | 0 | 0 | 0 | C, (0, 8) | (32, 128) | HIT_DIST along the ray = along forward = 44 |
  | `shootx-0.5` | +0.5 | 0 | 0 | R, (−12.02, 8) | (32.3, 128) | linear angle: (−11.24, 8) → texel x 19.8; sign inverted: L; HIT_DIST ray 45.6 vs forward 44 |
  | `shootx-neg0.5` | −0.5 | 0 | 0 | L, (+12.02, 8) | (31.7, 128) | linear: texel x 44.2; sign inverted: R |
  | `shootx-0.55` | +0.55 | 0 | 0 | R, (−13.22, 8) | (51.5, 128) | linear 38.6; mirrored bitmap 12.5; HIT_DIST ray 45.9 |
  | `shooty-0.25` | 0 | +0.25 | 0 | C, (0, 3.99) | (32, 192.1) | SHOOT_Y sign inverted: z 12.0 → texel y 63.9; HIT_DIST ray 44.18 |
  | `shooty-neg0.25` | 0 | −0.25 | 0 | C, (0, 12.01) | (32, 63.9) | inverted: 192.1 |
  | `tilt-0.1` | 0 | 0 | +0.1 | C, (0, 4.80) | (32, 179.3) | no shear on the ray: (0, 8) → 128; shear sign inverted: 76.7; HIT_DIST ray 44.12 |
  | `tilt-neg0.1` | 0 | 0 | −0.1 | C, (0, 11.20) | (32, 76.7) | no shear: 128; inverted: 179.3 |

  `SHOOT_Y ±0.5` would leave the 16-step room (8 steps at D = 44) and `PLAYER_TILT ±0.5` would
  miss the targets by 16 steps at D = 44; the chosen values keep every reading on a target.
- Measurement: `tools/oracle/measure_shoot.py <probe-dir>/[!_]*/` (one CSV row per variant:
  the letter, raw `HIN_*`/`HOUT_*`, whether `PRE` survived the second `PRINTFILE`, the nearest
  model for lateral / vertical / HIT_DIST, the `RESULT` check, and from the screenshot the
  bitmap half drawn on the screen left of each sprite and of the face; `--selftest` checks
  the verdicts on synthesized outputs).

## Observations
All runs 2026-10-10, patched V3.963 under native DOSBox-X, eight launches via `run-all.sh`,
every launch rc 0 with all eight outputs present and no compiler error in `OVCON.TXT`.
Values are `PRINT_VALUE` output (three decimals). "pred" = the shared-projection prediction
above; "Δ" = observed − predicted in texels (1 texel = 1/16 step = 0.83 px on screen at D = 44).

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| every run: state dump before the shot | `ANG=0.000 PZ=8.000 TILT=` as set, `SHX`/`SHY` as set, `SHRNG=500.000 SHFAC=1.000`; `TCK` 13.344–14.781 | the `SET SHOOT_X/SHOOT_Y` form is accepted; `TICKS` reads ≈ 13–15 after `WAITT 16` (units not probed) |
| every run: `OVSHO0.TXT` | `PRE` line present, then the five `HOUT_*` values | re-issuing `PRINTFILE "ovsho"` after the hit action did not truncate the file |
| every run: `OVHIT0.TXT` vs `OVSHO0.TXT` | exactly one letter; `HIN_D/X/Y/R/T` identical to `HOUT_D/X/Y/R/T` to the third decimal, including `TICKS` | `HIT_*` are valid inside `IF_HIT`; the hook ran within the `SHOOT` instruction (no tick advanced between the hook and the next statement) |
| every run: `RESULT` | 0.912 with `HIT_DIST` 44.000, `SHOOT_RANGE` 500, `SHOOT_FAC` 1 | = 1 − 44/500: the p.90 formula holds |
| `center` (0, 0, tilt 0) | C; HIT_DIST 44.000; HIT_X 32.832; HIT_Y 129.000 | pred (32, 128): Δx +0.83, Δy +1.0 |
| `shootx-0.5` | **R**; 44.000; HIT_X 33.176; HIT_Y 129.000 | pred R (32.3, 128): Δx +0.88; linear angle would read 19.8 (Δ −13.4); the ray length 45.6 is rejected at three decimals |
| `shootx-neg0.5` | **L**; 44.000; HIT_X 32.488; HIT_Y 129.000 | pred L (31.7, 128): Δx +0.79; linear 44.2 (Δ −11.7) |
| `shootx-0.55` | R; 44.000; HIT_X 51.193; HIT_Y 129.000 | pred (51.5, 128): Δx −0.34; linear 38.6, mirrored 12.5, both rejected; ray length 45.9 rejected |
| `shooty-0.25` | C; 44.000; HIT_X 32.832; **HIT_Y 64.000** | pred (32, 192.1) **rejected**; the sign-inverted reading (63.9) fits with Δy +0.1: **positive SHOOT_Y aims UP** (hit 4.0 steps above the eye); ray length 44.18 rejected |
| `shooty-neg0.25` | C; 44.000; HIT_X 32.832; **HIT_Y 193.000** | pred (32, 63.9) rejected; sign-inverted 192.1 fits (Δy +0.9): 4.0 steps below the eye |
| `tilt-0.1` | C; 44.000; HIT_X 32.832; HIT_Y 180.000 | pred (32, 179.3): Δy +0.7; the ray dropped 3.25 steps to z = 4.75 (frame-shear model 3.20; no-shear reading 128 rejected); positive tilt looks down, as in the tilt note |
| `tilt-neg0.1` | C; 44.000; HIT_X 32.832; HIT_Y 77.000 | pred (32, 76.7): Δy +0.3 |
| all eight: `HIT_DIST` | 44.000 | the targets' depth along the line of sight, whatever the lateral/vertical deviation |
| all eight: `HIT_X` / `HIT_Y` form | `HIT_X` carries fractions (32.832, 33.176, 51.193 …), `HIT_Y` is always an integer | `HIT_Y` looks rounded to a texel row; `HIT_X` is not |
| all eight: screenshot `OVSHT0.PCX` (640×480) | three sprites centered on columns 160.0 / 319.5 / 479.5; each reads cyan then orange left→right; the far-wall face reads yellow (columns 223–293) left of the center sprite, light blue (346–417) right of it, with a 1-px light-blue sliver at column 222 | predicted sprite centers 160.2 / 320 / 479.8 (map +12 on the LEFT: the handedness of the tilt note again); sprite bitmaps are **not mirrored**; the wall bitmap's left half sits at the wall's **second** vertex (y = +8, screen left) for the viewer in the wall's left region |

Residual summary (texels): lateral +0.83, +0.88, +0.79, −0.34; vertical +1.0 (×4 at the eye
line), +0.1, +0.9, +0.7, +0.3. Every residual is under one texel (0.83 px); every competing
reading is off by ≥ 11 texels (lateral) or ≥ 51 texels (vertical sign, shear).

## Conclusion
Confirmed for patched V3.963, `PLAYER_ARC` 1, `ASPECT` 1, `S640x480`:

1. **The `SHOOT` ray goes through the renderer's projection.** Lateral tangent
   `SHOOT_X · tan(PLAYER_ARC/2)` per unit of depth, positive `SHOOT_X` toward the screen right
   (map −y when facing +x); a linear-angle mapping is rejected by 12–13 texels.
2. **Positive `SHOOT_Y` aims up**, with the vertical half-tangent magnitude
   `(2/3) · tan(PLAYER_ARC/2)` per unit (`ASPECT` 1): `SHOOT_Y ±0.25` moved the hit ±4.0 steps
   at 44 steps. This is the opposite of the sign OpenVirtue had assumed.
3. **The `PLAYER_TILT` shear moves the ray with the frame**: tilt +0.1 lowered the `SHOOT_Y = 0`
   hit by 3.25 steps at 44 steps (model `−tilt · SCREEN_HGT / f_v · D` = 3.20), tilt −0.1 raised
   it the same amount.
4. **`HIT_DIST` is the depth of the hit along the line of sight** (`PLAYER_ANGLE` direction),
   not the length of the deviated ray: 44.000 in all eight runs, including the shots whose ray
   is 45.6–45.9 steps long. All eight hits were billboards standing at the same depth, so
   "depth of the hit point", "depth of the thing's position" and "distance to the sprite
   plane" coincide here; the wall rule OpenVirtue uses (trace distance × cos deviation) is
   the natural extrapolation, not an observation. `RESULT = SHOOT_FAC · (1 − HIT_DIST / SHOOT_RANGE)`.
5. **`HIT_X`/`HIT_Y` count from the bitmap's upper-left corner as drawn**: `HIT_X` from the
   sprite's screen-left edge (the off-center shot read 51.2, where a mirrored bitmap would read
   12.5), fractional; `HIT_Y` from the top (manual p.90), integer. Because the targets are
   centered on the eye line, the data alone fix the triple (`HIT_Y` origin, `SHOOT_Y` sign,
   shear sign) only up to a joint flip: a bottom origin with positive `SHOOT_Y` down and a
   ray shear opposite to the frame reproduces the same five `HIT_Y` values. The top origin
   is taken from p.90 and the frame-following shear from the tilt note, which pins the rest.
   Both skills are valid inside `IF_HIT`, which runs within the `SHOOT` instruction.
6. Harness: `HIT_*` carry a small positive bias at a dead-center hit (+0.8 texel in x, +1 texel
   in y), consistent with the ray passing about half a pixel right of and below the model's
   center (cf. the 319.5/240.5 centers measured in the FOV note) and `HIT_Y` rounding; this
   probe cannot resolve it (one texel = 0.83 px here).
7. **Orientation**: thing bitmaps are drawn unmirrored (left half on the screen left). A wall
   viewed from its left region shows the bitmap's left half at the wall's **second** vertex,
   i.e. u = 0 at v2 growing toward v1 (the texture reads left→right for the viewer on the
   wall's left side).

## Confidence
high for 1, 3, 4 (billboards), 5 and 6: eight runs, every reading within one texel of the
shared-projection model with the corrected `SHOOT_Y` sign; the linear lateral mapping is
rejected by ≥ 11 texels, the vertical sign and the no-shear reading by ≥ 51 texels, and
`HIT_DIST` is resolved at three decimals. medium for 2's vertical *magnitude*: a linear-angle
vertical mapping (`SHOOT_Y · atan` of the half-tangent) would read texel 66.4 / 189.6 against
the tangent model's 63.9 / 192.1 and the observed 64 / 193, a margin of only 2–3 texels with
a known +0.5..1 texel bias in play. medium for 7's wall orientation: one wall, viewed from its
left region only.

## Open Questions
- `HIT_DIST` for a wall hit, in particular a wall oblique to the line of sight (depth of the
  hit point vs. ray length vs. perpendicular distance). One variant settles it: `SHOOT_X` 1.0
  at arc 1 passes the right target and hits the side wall at y = −24, x ≈ 43.9, where the
  candidates read 43.9 / 50.0 / 24.
- The vertical magnitude at a larger margin: `SHOOT_Y ±0.45` separates the tangent (7.21
  steps) from a linear angle (6.97) by ≈ 4 texels while staying on the 16-step target; and a
  target not centered on the eye line (e.g. 12 steps tall) fixes the `HIT_Y` origin
  independently of the `SHOOT_Y` sign.
- Wall texture orientation seen from the wall's right region (does the engine flip per side so
  it reads left→right from both sides, or keep u = 0 at v2?), and the sense of `OFFSET_X`
  relative to u. One two-sided fixture would answer both.
- `SHOOT` at `PLAYER_ARC ≠ 1` or `ASPECT ≠ 1` (does the lateral tangent scale with the arc and
  the vertical with `ASPECT`); `--shoot-x/--shoot-y` reruns at other skills would cover it.
- The half-texel bias (ray through a pixel center vs. edge) and why `HIT_Y` is an integer while
  `HIT_X` is not.
- Which measure `SHOOT_RANGE` limits (assumed: the same depth as `HIT_DIST`, since `RESULT`
  reaches 0 exactly there).
- `TICKS` ≈ 13–15 after `WAITT 16` at world start: the skill's units or start time.

## Tests To Add Or Update
Updated 2026-10-10: `ProjectionModelTests.ShootRay_Center_FollowsTiltAndEdgesFollowHalfTangents`
(positive `SHOOT_Y` up, +0.25 → 4.0 steps at 44); `WdlRuntimeTests.Runtime_ShootInstruction_ShootYUsesProjectedVerticalAim`,
`_AspectTwoWidensVerticalAim`, `_NegativeTiltRaisesCenterRayLikeShootY` (`SHOOT_Y +1` aims at
the top edge; `HIT_DIST` is the depth 4), `_PositiveShootXAimsToScreenRight` (also `HIT_DIST`
4, not the 4.45 ray length, and `RESULT`). Added: `Runtime_ShootInstruction_WallHitXCountsFromTheSecondVertex`,
`WorldHandednessTests.Wall_TextureUStartsAtTheSecondVertex_AsTheOriginalShowsFromTheLeftRegion`.

## Implementation Notes
Implemented 2026-10-10 in engine commit `2a817da`: `ProjectionModel.ShootRay` returns
`+SHOOT_Y · VerticalHalfTangent + CenterSlope`; `WdlRuntime.ShootFromPlayer`/`FindClosestHit`
report billboards' center depth and walls' trace distance × cos(deviation) as `HIT_DIST`
(and apply `SHOOT_RANGE` to that measure); `MeshBuilder.EmitWallSegment` puts u = 0 at v2;
`CreateWallHitCandidate` counts the wall `HIT_X` from v2. `HIT_X`/`HIT_Y` stay integer texel
indices in OpenVirtue (sub-texel precision is not needed for parity).
