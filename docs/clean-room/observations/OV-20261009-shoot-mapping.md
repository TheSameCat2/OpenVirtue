# OV-20261009-shoot-mapping: Do SHOOT_X/SHOOT_Y aim through the renderer's projection, and what do HIT_DIST/HIT_X/HIT_Y report?
Date: 2026-10-09
Status: proposed (carriers prepared, not yet launched)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
For a `SHOOT` fired by the player at the documented defaults (`PLAYER_ARC` 1, `ASPECT` 1,
`VIDEO S640x480`):

1. **Lateral mapping and sign.** Is the ray's lateral deviation for `SHOOT_X = s` the one
   through the screen column `s · SCREEN_WIDTH/2` from the center, i.e. a lateral tangent of
   `s · tan(PLAYER_ARC/2)` (what OpenVirtue assumes), or a linear angle `s · PLAYER_ARC/2`?
   Does positive `SHOOT_X` aim to the original's screen right (map −y when facing +x,
   OV-20261009-tilt-model-shear-vs-pitch)?
2. **Vertical mapping and sign.** Does `SHOOT_Y = s` give a vertical slope of
   `−s · (2/3) · ASPECT · tan(PLAYER_ARC/2)` (positive = down the screen), i.e. the vertical
   half-tangent of OV-20261009-pixel-aspect-video-modes?
3. **Tilt.** With `SHOOT_Y = 0` and `PLAYER_TILT ≠ 0`, does the ray follow the y-shear
   (slope `−PLAYER_TILT · SCREEN_HGT / f_v`), or does `SHOOT` ignore the tilt?
4. **Hit skills.** Is `HIT_DIST` the distance along the ray or along the line of sight? Are
   `HIT_X`/`HIT_Y` texels from the bitmap's upper-left corner (manual p.90) with the bitmap's
   left edge on the screen left? Are they valid inside the target's `IF_HIT` action, which
   the manual does not say (p.90 only: "after the SHOOT instruction")? Does `RESULT` follow
   `SHOOT_FAC · (1 − HIT_DIST / SHOOT_RANGE)` (p.90)?
5. **By-products.** Which half of a thing bitmap is drawn on the screen left (sprite
   mirroring), and which half of a wall bitmap sits toward the wall's first vertex (wall
   texture orientation, the open question ADR-0009 left unprobed)?

## Why It Matters
`WdlRuntime.ShootFromPlayer` builds the ray from `ProjectionModel.ShootRay`, i.e. it assumes
the hitscan shares the renderer's measured projection and uses OpenVirtue's own sign
conventions (positive `SHOOT_X` clockwise = screen right, positive `SHOOT_Y` down). The
four hitscan tests (`Runtime_ShootInstruction_ShootYUsesProjectedVerticalAim`,
`_AspectTwoWidensVerticalAim`, `_NegativeTiltRaisesCenterRayLikeShootY`,
`_PositiveShootXAimsToScreenRight`) encode that assumption, not an observation. The retail
weapon actions fire `SHOOT`; whether a shot lands where the crosshair points, and where a
bullet-hole `ATTACH` is placed from `HIT_X`/`HIT_Y`, depends on these answers. The sprite and
wall texture orientation decides whether the renderer must flip `u` (ADR-0009 deliberately
did not).

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
  y = −8 to the vertex at y = +8. Palette with no `RANGE` (shading off), `DITHER 0`, `NEXUS 14`.
- `IF_START` (`probe_start`): `SET PLAYER_ARC/PLAYER_TILT/PLAYER_SIZE/PLAYER_Z`, `SET SHOOT_X`,
  `SET SHOOT_Y`, `MOTION_BLUR 0`, `RENDER_MODE 2`, `WAITT 16`, the skill dump to `OVPRB0.TXT`
  (the projection probes' labels plus `SHX`, `SHY`, `SHRNG`, `SHFAC`, `TCK`), then
  `PRINTFILE "ovsho"`, `PRINT_STRING "PRE"`, **`SHOOT;`**, `PRINTFILE "ovsho"` again and the
  five `HOUT_*` values (`HIT_DIST`, `HIT_X`, `HIT_Y`, `RESULT`, `TICKS`), `WAITT 4`,
  `SCREENSHOT "ovsht", 0`, `OVEND0.TXT`, `EXIT`. Each target's `IF_HIT` action selects
  `OVHIT0.TXT`, writes its letter (`L`/`C`/`R`) and the same five skills as `HIN_*`. Labels
  stay ≤ 12 characters (compiler limit, FOV note).
- Variants (`S640x480`, arc 1, aspect 1) and the predicted hit under the shared-projection
  model (ray from the eye (0, 0, 8) along +x; lateral tangent `SHOOT_X · tan(0.5)` toward
  map −y; slope `−SHOOT_Y · (2/3) · tan(0.5) − PLAYER_TILT · 480 / 659`; target plane
  x = 44; texel from the target's upper-left = (+y edge, z = 16) at 16 px/step), next to the
  competing readings `geometry.json` also carries:

  | variant | SHOOT_X | SHOOT_Y | TILT | predicted target, map (y, z) | predicted texel (x, y) | competing readings |
  |---|---|---|---|---|---|---|
  | `center` | 0 | 0 | 0 | C, (0, 8) | (32, 128) | HIT_DIST along the ray = along forward = 44 |
  | `shootx-0.5` | +0.5 | 0 | 0 | R, (−12.02, 8) | (32.3, 128) | linear angle: (−11.24, 8) → texel x 19.8; sign inverted: L; HIT_DIST ray 45.6 vs forward 44 |
  | `shootx-neg0.5` | −0.5 | 0 | 0 | L, (+12.02, 8) | (31.7, 128) | linear: texel x 44.2; sign inverted: R |
  | `shootx-0.55` | +0.55 | 0 | 0 | R, (−13.22, 8) | (51.5, 128) | linear 38.6; mirrored bitmap 12.5; linear + mirrored 25.4; HIT_DIST ray 45.9 |
  | `shooty-0.25` | 0 | +0.25 | 0 | C, (0, 3.99) | (32, 192.1) | SHOOT_Y sign inverted: z 12.0 → texel y 63.9 |
  | `shooty-neg0.25` | 0 | −0.25 | 0 | C, (0, 12.01) | (32, 63.9) | inverted: 192.1 |
  | `tilt-0.1` | 0 | 0 | +0.1 | C, (0, 4.80) | (32, 179.3) | no shear on the ray: (0, 8) → 128; shear sign inverted: 76.7 |
  | `tilt-neg0.1` | 0 | 0 | −0.1 | C, (0, 11.20) | (32, 76.7) | no shear: 128; inverted: 179.3 |

  `SHOOT_Y ±0.5` would leave the 16-step room (8 steps at D = 44) and `PLAYER_TILT ±0.5` would
  miss the targets by 17 steps; the chosen values keep every reading on a target.
- Measurement: `tools/oracle/measure_shoot.py <probe-dir>/[!_]*/` (one CSV row per variant:
  the letter, raw `HIN_*`/`HOUT_*`, whether `PRE` survived the second `PRINTFILE`, the nearest
  model for lateral / vertical / HIT_DIST, the `RESULT` check, and from the screenshot the
  bitmap half drawn on the screen left of each sprite and of the face; `--selftest` checks
  the verdicts on synthesized outputs).

## Observations
Record facts, not implementation guesses.

| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| 2026-10-09 carriers prepared (`ovtool oracle prepare shoot-mapping --runtime _research/oracle-runs/runtime/VRUN-patched.EXE`) | eight carrier folders under `_research/oracle-runs/OV-20261009-shoot-mapping/`, generated WDL/WMP accepted by OpenVirtue's own parsers; not launched | awaiting the user's go for the first DOSBox-X launch of the session |

## Conclusion
Pending the runs.

## Confidence
low

Nothing has been observed yet; the table above holds predictions only.

## Open Questions
- Whether `IF_HIT` runs synchronously inside `SHOOT` or on a later frame (the `TICKS` values
  inside the hook and after the instruction will say).
- Whether re-issuing `PRINTFILE` truncates the file (the `PRE` line) — a harness fact.
- Half-pixel convention of the ray (does `SHOOT_X = 0` go through column 320 or 319.5); the
  texel resolution of 1/16 step is coarser than that at D = 44 (one texel = 0.0625 step =
  0.8 px on screen), so this probe cannot resolve it.
- `SHOOT` with `PLAYER_ARC ≠ 1` or `ASPECT ≠ 1` (does the lateral tangent scale with the arc,
  the vertical with `ASPECT`) is not in the default batch; `--shoot-x/--shoot-y` rerun at
  other skills would cover it.

## Tests To Add Or Update
- `WdlRuntimeTests.Runtime_ShootInstruction_PositiveShootXAimsToScreenRight` (keep if the
  sign holds, flip if not), `_ShootYUsesProjectedVerticalAim`, `_AspectTwoWidensVerticalAim`,
  `_NegativeTiltRaisesCenterRayLikeShootY`; `ProjectionModelTests.ShootRay_*`.
- If `HIT_DIST` is along the ray (not forward), check `WdlRuntime.FindClosestHit`'s
  `distanceScale` handling; if `HIT_X` counts from the other edge or the bitmap is mirrored,
  adjust the `u` computation there and `SpriteBuilder`/`LevelWindow` mirroring.
- Wall texture orientation: `MeshBuilder`'s `u` direction (currently v1 → v2) if the face's
  yellow half is not toward the first vertex.

## Implementation Notes
Nothing implemented from this note yet. OpenVirtue keeps the shared-projection assumption
until the data says otherwise.
