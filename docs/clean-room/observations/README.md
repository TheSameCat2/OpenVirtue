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
  They also run during a FLIC (OV-20261015-flic-play-blocking).

## FLIC probe (2026-10-15 — confirmed and implemented)

- `OV-20261015-flic-play-blocking.md` — `PLAY_FLICFILE` starts the flic and the action goes on
  in the same frame. During the flic the global `EACH_TICK` and `EACH_SEC` slots and `WAITT`
  actions keep running, and key hooks fire. `STOP_FLIC` from a hook ends the flic by the next
  frame. `FLIC_FRAME` is the 1-based frame on screen and 0 when no flic plays. The scripted Space
  came from DOSBox-X `AUTOTYPE`. Six DOSBox-X runs on a synthetic FLC; implemented in
  `WdlInterpreter`, `WdlRuntime` and `RuntimeHost`.
- `OV-20261015-flic-scheduler-detail.md` — the flic runs one scheduler cycle per flic frame, with
  `TIME_CORR` the frame delay over 64 ms (1.09375 at 70 ms). `WAIT n` resumes n cycles after its
  arming cycle. `WAITT n` counts its arming cycle's `TIME_CORR` too and resumes once more than
  n − 1 ticks are counted, in world frames as well. During the flic, player and actor movement,
  animation, object and region actions and region and proximity events are paused, and mouse
  hooks fire. A second `PLAY_FLICFILE` replaces the playing flic. A missing file is skipped
  (`FLIC_FRAME` unchanged); requested during a flic, it faults this runtime. Mouse clicks came
  from an X11 helper (`tools/oracle/x11_click.py`). Fifteen DOSBox-X runs; implemented in
  `WdlRuntime`, `Timing` and `RuntimeHost`.

## Proximity and region event probe (2026-10-19 — confirmed and implemented)

- `OV-20261019-proximity-events.md` — `IF_NEAR` fires once when the player comes within `DIST`
  and `IF_FAR` once when they leave, checked every cycle, so walks, beams and moving actors all
  count. For things and actors `DIST` is compared with |dx| + |dy| (also `MY.DISTANCE`). A write
  to `PLAYER_X/PLAYER_Y` keeps the player's region and fires no region event. `LOCATE` fires the
  old region's `IF_LEAVE`, then the new one's `IF_ENTER`. Crossing a wall enters the region on the
  side moved onto: the first WMP region is left of v1 → v2. The repeated `IF_NEAR` of the
  flic-detail controls came from actor contact (still open). Five DOSBox-X runs; implemented in
  `WdlRuntime` and `LevelCollisionWorld`.

## Wall distance probe (2026-10-19 — confirmed and implemented)

- `OV-20261019-wall-distance.md` — a wall's `MY.DISTANCE` and its `DIST` test use |dx| + |dy|
  between the player and the nearer of the wall's two vertices, so the centre of a long wall
  does not fire. The value is computed once per cycle before the actions, for walls in or out of
  view and outside `CLIP_DIST`. Passing through a wall fires `IF_NEAR` regardless of `DIST`, and a
  wall's `EACH_TICK` only runs within the player's `CLIP_DIST` (neither implemented yet). Three
  DOSBox-X runs; implemented in `WdlRuntime`.
