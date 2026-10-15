# Observation Records

This directory holds sanitized clean-room observation notes for hidden
Acknex-3/Saints of Virtue runtime invariants.

Use `../observation-template.md` for new records.

Rules:

- Commit Markdown notes only.
- Do not commit original assets, raw captures, save files, executable bytes,
  decompiled/disassembled output, or third-party source excerpts.
- Keep raw local artifacts under `_research/oracle-runs/` or `artifacts/oracle/`.
- Give each note a stable `OV-YYYYMMDD-short-topic` ID.

## Projection probes (2026-10-09 audit — resolved and implemented)

Three geometric probes that gate the GL renderer design (ADR-0008); static scenes, pixel
measurements, no timing involved. All three ran on 2026-10-09 (19 DOSBox-X runs; 21 launches,
the FOV probe's first two being a failed and a diagnostic launch) and are
implemented in `OpenVirtue.Engine.Rendering.ProjectionModel`:

- `OV-20261009-projection-fov-player-arc.md` — horizontal FOV is exactly `PLAYER_ARC` radians
  (the old code treated it as a vertical FOV).
- `OV-20261009-pixel-aspect-video-modes.md` — vertical scale 0.75·H/(`ASPECT`·tan(arc/2)), no
  pixel-aspect pre-stretch; every VGA mode is presented at 4:3.
- `OV-20261009-tilt-model-shear-vs-pitch.md` — `PLAYER_TILT` is a y-shear of −tilt·H rows
  (positive looks down); also records the screen handedness (map +y on the left when facing +x;
  implemented by ADR-0009).

## SHOOT probe (2026-10-10 — confirmed and implemented)

- `OV-20261009-shoot-mapping.md` — `SHOOT_X`/`SHOOT_Y` aim through the same projection
  (lateral tangent, positive `SHOOT_X` right, positive `SHOOT_Y` **up**, the tilt shear on the
  ray), `HIT_DIST` is the hit's depth along the line of sight, `HIT_X`/`HIT_Y` count from the
  bitmap's upper-left as drawn and are valid inside `IF_HIT`, `RESULT` follows the manual's
  formula; thing bitmaps are unmirrored and a wall's bitmap starts at its second vertex for a
  viewer in its left region. Eight DOSBox-X runs; implemented in `ProjectionModel.ShootRay`,
  the `WdlRuntime` hitscan and `MeshBuilder`.

## BRANCH probe (2026-10-15 — confirmed and implemented)

- `OV-20261015-branch-tail-jump.md` — `BRANCH` is a tail jump: the target runs in place of the
  current action and nothing after the `BRANCH` runs, not even the rest of an enclosing
  `IF`/`WHILE`. The branching action's caller resumes as it would for the action itself, and
  chains do not nest (1000 without a `WAIT`). A `NULL` synonym target is a no-op, and an
  undeclared name is a compile error. Eleven DOSBox-X runs; implemented in `WdlInterpreter`.

## EACH_SEC probe (2026-10-15 — confirmed and implemented)

- `OV-20261015-each-sec-cadence.md` — the undocumented global `EACH_SEC` slots that the retail
  title and levels use for their timeouts. There are 16 slots (slot 17 is a compile error). All
  share the runtime's second and run, again every second, in the frame where `SECS` goes up,
  just before the global `EACH_TICK` slots. `SECS` is a whole-second count. A waiting slot action
  goes on while the next second starts another. Nine DOSBox-X runs; implemented in `WdlRuntime`.
  Open: whether the slots run during a FLIC.
