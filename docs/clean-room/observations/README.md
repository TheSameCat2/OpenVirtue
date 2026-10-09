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

## Proposed probes (2026-10-09 projection audit)

Three geometric probes that gate the GL renderer design (ADR-0008). Static scenes, pixel
measurements, no timing involved — cheap, and they should run **before** the renderer port:

- `OV-20261009-projection-fov-player-arc.md` — horizontal FOV vs `PLAYER_ARC` (current code
  treats it as a vertical FOV in radians).
- `OV-20261009-pixel-aspect-video-modes.md` — 320x200 pixel aspect / `ASPECT` skill (current
  presentation is 16:10 square-pixel).
- `OV-20261009-tilt-model-shear-vs-pitch.md` — y-shear vs true pitch for `PLAYER_TILT`.
