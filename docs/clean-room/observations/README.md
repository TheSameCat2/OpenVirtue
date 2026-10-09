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
measurements, no timing involved. All three ran on 2026-10-09 (19 DOSBox-X launches) and are
implemented in `OpenVirtue.Engine.Rendering.ProjectionModel`:

- `OV-20261009-projection-fov-player-arc.md` — horizontal FOV is exactly `PLAYER_ARC` radians
  (the old code treated it as a vertical FOV).
- `OV-20261009-pixel-aspect-video-modes.md` — vertical scale 0.75·H/(`ASPECT`·tan(arc/2)), no
  pixel-aspect pre-stretch; every VGA mode is presented at 4:3.
- `OV-20261009-tilt-model-shear-vs-pitch.md` — `PLAYER_TILT` is a y-shear of −tilt·H rows
  (positive looks down); also records the screen handedness (map +y on the left when facing +x).
