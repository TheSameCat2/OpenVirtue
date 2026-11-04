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
  the `WdlRuntime` hitscan and `MeshBuilder`. A 19-run sweep (2026-10-29) adds:
  - the shot goes through the whole pixel `floor(W/2 + SHOOT_X·W/2)`, `floor(H/2 − SHOOT_Y·H/2)`;
  - a sprite's fractional `HIT_X` is the renderer's texel step at that column, from the projected
    left edge plus 0.714 pixels, in 22.10 steps;
  - 24 of 27 carriers replay.

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
  `WdlInterpreter`, `WdlRuntime` and `RuntimeHost`. Round 2: a flic started in `IF_START` leaves
  that cycle the start-up cycle; the flic's cycles sit a frame delay apart on the `TICKS` grid
  from `PLAY_FLICFILE`, a longer host frame taken whole and made up by the next; `CEIL_HGT` reads
  0 after `PLAY_FLICFILE`.
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

## Wall pass-through and CLIP_DIST probe (2026-10-19 — confirmed and implemented)

- `OV-20261019-wall-pass-clip.md` — a wall's `IF_NEAR` also fires in every cycle the player's
  movement passes through it (segment crossing, end points included) or pushes against it, while
  the player is outside its `DIST`. No `IF_FAR` follows, and with `DIST 0` this is the only way it
  fires. A beam across a wall does not count. A wall's `EACH_TICK` runs only while its `DISTANCE` is
  below the player region's `CLIP_DIST` (border excluded). A thing's and an actor's run only while
  theirs is below `CLIP_DIST × THING_DIST` or `× ACTOR_DIST` (both default 1). Six DOSBox-X runs;
  implemented in `LevelCollisionWorld`, `Player` and `WdlRuntime`.

## Region borders, contact, LIBER/BERKELEY and the CLIP_DIST gate (2026-10-19 — confirmed and implemented)

- `OV-20261019-clip-contact.md` — crossing a wall between two regions fires its `IF_NEAR`, with or
  without `PASSABLE`. Pushing against a solid thing or actor fires its `IF_NEAR` in every pushing
  cycle while the player is outside its `DIST`, with no `IF_FAR`. Outside its border
  (`CLIP_DIST × THING_DIST`/`ACTOR_DIST`) a thing or actor runs no `EACH_TICK` or `EACH_CYCLE`,
  does not move and is not `DIST`-tested, so leaving the border fires no `IF_FAR`. `LIBER` lifts
  all of that. `CLIP_DIST` is the smaller of the player's region's and the object's region's. A
  `BERKELEY` object is active only while the player is within its `DIST`, even in view, and fired
  no `IF_NEAR`/`IF_FAR`. A `TARGET` naming a way makes the actor patrol it. The player walked onto
  floors 14 and 16 steps higher, so contact with an unenterable region border is still open.
  Seventeen DOSBox-X runs; implemented in `LevelCollisionWorld`, `Player` and `WdlRuntime`. Addendum 2026-11-04: in the first cycle a
  `BERKELEY` object ahead within the view's arc runs its `EACH_TICK` once outside `DIST`. Rounds
  2–3 (2026-11-04, seven runs): a standing thing or actor starts its `DISTANCE` when first
  refreshed within 1.5 × its border and then reads it at any range; an actor that has moved starts
  at any range. Things and actors run from the last placed to the first whatever their kind, the
  `SKIP_FRAMES` near range stops at the border, and a `FOLLOW` actor targets `PLAYER_X`/`PLAYER_Y`.
  Open: a moved actor's sentinel beyond about 39 while another sprite is drawn near. Rounds 4–5
  (2026-11-04, eight runs): an actor's push is the player's own motion: a step toward the actor is
  held, any other is taken and the player pushed from where it ended, a push a wall refuses or that
  moves nothing restores the cycle's start, and the player walks freely from the actor's centre;
  a push a wall stops lands half way to the wall's line (refused whole within the actor's radius
  of it, a guessed bound). Open: the lateral nudge after a step aside, actor-push's wall edge.
  Rounds 6–10 (ten carriers): the far sentinel is the unstarted state again, ended by a refresh
  within 1.5 × the border; a beam into or out of a follower's contact circle un-starts the far
  objects, a drawn sprite, a pushing actor or a stacked spot does not. Not implemented (the walk
  case stays unexplained).

## Region-border climb and headroom (2026-10-19 — confirmed and implemented)
- `OV-20261019-border-climb.md` — a `PASSABLE` wall lets the player through whatever the
  heights. Through any other wall between two regions the player enters only if the new floor is
  less than `PLAYER_CLIMB` above the feet (`PLAYER_Z − PLAYER_SIZE`, so a raised eye climbs
  higher), and the eye lies above the new floor and below the new ceiling. `PLAYER_SIZE` and the
  headroom count only through those, and a region with floor = ceiling always holds. Pushing
  against a border that blocks fires its `IF_NEAR` in every pushing cycle, right after that
  cycle's move (2026-11-03 addendum). The runtime keeps the
  eye within the floor + 0.35 and the ceiling − 0.35 (implemented with OV-20261019-eye-clamp).
  `LOCATE` of the player sets `FLOOR_HGT` and `CEIL_HGT` at once (2026-11-03 addendum).
  Fourteen DOSBox-X runs; implemented in `Player`.
- `OV-20261019-eye-clamp.md` — `PLAYER_VZ` moves the eye freely, and the feet sink below the
  floor. One frame moves it by `PLAYER_VZ × max(TIME_CORR, 4)`. After every frame the runtime
  clamps the eye to [floor + 0.35, ceiling − 0.35] of the player's region, the ceiling winning.
  The clamp also catches a scripted `PLAYER_Z`, leaves `PLAYER_VZ` alone and adds its correction
  to `IMPACT_VZ`. Solid walls and blocking region borders stop a step that would end within
  `PLAYER_WIDTH` and hold the player `PLAYER_WIDTH` + 51/1024 away, alternating by 4/1024 while it
  pushes; `PLAYER_WIDTH` is not held at the manual's 1.2 minimum. Each frame's step is
  `PLAYER_VX × TIME_CORR` rounded to 1/1024 (2026-11-03 addendum). Ten DOSBox-X runs (six at
  forced CPU speeds); implemented in `Player`, `LevelCollisionWorld` and `WdlPlayerMotion`.

## Contact, border kinds and CLIP_DIST detail (2026-10-19/20 — confirmed and implemented)

- `OV-20261019-actor-contact.md` — contact with a thing or actor is a circle of half its width
  (× `THING_WIDTH`/`ACTOR_WIDTH`) plus `PLAYER_WIDTH`. An actor without `CAREFULLY` pushes the
  player ahead of it and fires `IF_NEAR` in every pushing cycle. A `CAREFULLY` one stops short,
  halving its step until the point two steps ahead is clear, and fires `IF_NEAR` while blocked.
  A beam inside a solid object is pushed straight out at the next move. `DIST` is strict (`<`).
  Addendum 2026-11-03: the push is the start-up push-out's 22.10 distance from the actor's centre
  (the lateral part scaled, 4/1024 aside where there is none), once a cycle with the skills already
  pushed; a push a wall would stop is refused whole.
  For things and actors the eye must also be less than `DIST` above or below the texture's
  centre, which is why small `DIST`s on low objects never fired. `DISTANCE` reads 65535.999
  until the player first comes within 1.5 × the `CLIP_DIST` border. Eighteen DOSBox-X runs;
  implemented in `WdlRuntime`, `Player`, `LevelCollisionWorld` and `WdlPlayerMotion`.
- `OV-20261019-border-kinds.md` — `IMPASSABLE` stops the player unless `PASSABLE` and always stops
  a `CAREFULLY` actor (1.05 from the wall for a 2-wide one). `INVISIBLE` walls never stop the player
  and fire nothing when a free-standing one is passed; an invisible region border ignores the
  climb and still fires `IF_NEAR` on crossing. A `CAREFULLY` actor climbs a rise below
  `ACTOR_CLIMB` (default 1.1) and below its own height; `PLAYER_CLIMB` does not apply.
  `SKIP_FRAMES` moves unseen actors outside `DIST` every 6th cycle (implemented with
  OV-20261020-view-activity). The eye must be strictly above a new floor. Fifteen DOSBox-X runs; implemented in `Player`,
  `LevelCollisionWorld` and `WdlRuntime`.
- `OV-20261019-clip-detail.md` — outside its border a thing's or actor's texture animation is
  paused. A `BERKELEY` wall runs its `EACH_TICK` only while in view (refined and implemented in
  OV-20261020-view-activity). A wall's
  `DIST` test is gated by the border like a thing's. A wall's border is the player's region's
  `CLIP_DIST` alone, even between two regions. `INVISIBLE` walls run no `EACH_TICK`. Five
  DOSBox-X runs (one more failed to compile); implemented in `WdlRuntime`. Addendum 2026-11-04: a `CAREFULLY` long step may not end in a region
  entered through a border it may not cross; through it and out again passes. Round 8 (2026-11-04): off the axis the push is along the line
  of centres; on it the 4/1024 minimum applies to the player's displacement; the `EXIT` cycle moves
  no actor.

## View activity (2026-10-20 — confirmed and implemented)

- `OV-20261020-view-activity.md` — `VISIBLE` is set while some of a sprite or wall is on the screen
  in the last drawn frame: in the `PLAYER_ARC` cone (counting its width), not behind a wall or a
  region step, and between the screen's top and bottom. `SKIP_FRAMES` follows it: an actor that is
  not visible, outside its `DIST`, without `FAR` and at least 20 from the player (|dx| + |dy|) moves
  every (`SKIP_FRAMES` + 1)th cycle counted from its own last move, by that many cycles' step. Such
  a step, for a `CAREFULLY` actor, is tested at its end only and can jump walls and raised regions.
  A `BERKELEY` wall runs its `EACH_TICK` while visible or while the player is within its `DIST`.
  Rounds 6–9: the near range is ≤ 20 of the actor's `DISTANCE` as last refreshed, and the original
  refreshes every actor's `DISTANCE` only in cycles in which the player moved or an actor in front
  of the player moved (which explains range-shape's actor that never skipped). `TRANSPARENT` and
  `INVISIBLE` walls hide nothing, nor do sprites. Every wall gets `VISIBLE`; walls and actors keep
  `SEEN` once seen.
  Twenty-one DOSBox-X launches (seventeen usable); implemented in `ViewTest` and `WdlRuntime`
  (ADR-0010). The screen's top and bottom are half a row inside its edges: a sprite must cover a
  row's centre (2026-11-03 addendum, read from actor-events `way`). A standing actor's skip count
  runs on unconsumed, so it moves the moment it gets a speed and skips from there; a walker on the
  player's own plane (depth under 1/1024) is not in front (2026-11-03 addendum). A beam within
  `PLAYER_WIDTH` of a wall is held at the wall's clearance in the next cycle, and only a
  free-standing wall behind a long step's end refuses it, not an `IMPASSABLE` border (2026-11-03
  addendum, read from view-pillar and border-kinds imp-border).

## Distance refresh (2026-10-21 — confirmed and implemented)

- `OV-20261021-distance-refresh.md` — `IF_NEAR`/`IF_FAR` and the `DIST` lift of `SKIP_FRAMES` read
  things' and actors' `DISTANCE` as last refreshed. It is refreshed for all of them at once when the
  view changes (a walk or beam, a turn, a tilt, the eye height, `PLAYER_ARC`) or an actor in front
  of the player walks; a script's `SET` of a thing's or actor's position refreshes nothing, its own
  `DISTANCE` included. A region is `VISIBLE` while some of its floor, ceiling or walls is on the
  screen, and keeps `SEEN`. Six DOSBox-X runs; implemented in `ViewTest` and `WdlRuntime`. A
  standing way actor's first move, which only arrives at its point, refreshes like a step
  (2026-11-03 addendum: thing-refresh's refresh before the first step).

## Print output (2026-10-29 — confirmed from existing runs and implemented)

- `OV-20261029-print-output.md` — `PRINTFILE "name", 0` writes `NAME0.TXT` (number unpadded, upper
  case), `PRINT_VALUE` appends the three-decimal value with nothing after it, and `\n` in a
  `PRINT_STRING` is a bare line feed. Read from the trace files of the earlier runs; the oracle-diff
  harness needs it to compare files byte for byte. Implemented in `WdlRuntime`/`WdlInterpreter`.

## Scheduler order (2026-10-29 — confirmed and implemented)

- `OV-20261029-scheduler-order.md` — `IF_START` and the first cycle read `TIME_CORR` 1 and
  `TICKS` 0; `TICKS` counts from the second cycle, and `IF_START` counts as a cycle for the `WAIT`s
  and `WAITT`s it arms (so `WAITT 16` there ends at `TICKS` 13.4 at 60 Hz). A cycle runs the resumed waits, the
  `EACH_SEC` slots, the global slots in slot order, then regions, actors, things and walls, each
  kind from the last placed to the first, each object's `EACH_TICK` before its animation step and
  `EACH_CYCLE`. A `SET EACH_TICK` counts in the same cycle. Regions too run from the last placed
  (round 2). Six DOSBox-X runs; implemented in `WdlRuntime`. Rounds 6 to 8: a key hook runs in
  the cycle after its resumed waits, the key's own hook before `IF_ANYKEY`, with the cycle's
  `TICKS` and count and the key skills at 1 while the key is down; a wait it arms counts that
  cycle like any other and resumes after the older waits; a second press while its action waits
  starts a second instance (hooks queued into the cycle).

## Number format (2026-10-29 — confirmed and implemented)

- `OV-20261029-numeric-edge.md` — skills are 22.10 fixed point (steps of 1/1024, ±2097152). Literals
  truncate and saturate, `+ − *` wrap in 32 bits, `INT` rounds down, `%` and the bitwise operators act
  on the raw bits, functions truncate their results and read −2097151.999 when undefined, and a
  division by zero is fatal in the original. Seven DOSBox-X runs; implemented in `WdlFixed`. The
  shutdown loses the selected print file's text since it was selected; the replay reproduces it
  under the opt-in `FatalErrors` switch, while a game goes on (2026-11-03 addendum).

## Motion timing (2026-10-29 — confirmed and implemented; animation phases in OV-20261029-anim-timing)

- `OV-20261029-motion-timing.md` — `FRICTION` defaults to 0 (not 0.5). `ACCEL v, a` integrates
  `(a − FRICTION·v)/INERTIA` per tick over `TIME_CORR`, a tick at a time, so the speed tends to
  `a/FRICTION`; it fits all 247 traced calls. `CYCLE` counts from 1, an object's `EACH_TICK` runs
  before its animation step, and a `ONESHOT` rests on its last phase. Phase lengths are answered by
  OV-20261029-anim-timing. Fourteen DOSBox-X runs; `ACCEL` implemented in
  `WdlInterpreter`.

## Actor events (2026-10-29 — confirmed and implemented)

- `OV-20261029-actor-events.md` — actors step `SPEED·TIME_CORR` in \|dx\| + \|dy\| toward a point or
  along their angle, arrive within 0.1 of a point (a step that reaches it is held at the point's
  coordinate on each axis, so a part that rounds to nothing leaves that axis short), advance
  `WAYPOINT` before `IF_ARRIVED` (with `TARGET_X/Y` still the point reached), trigger `IF_ARRIVED` in
  every cycle at a `VERTEX` or object target and `IF_HIT` in every cycle a `BULLET` is blocked, and
  move in their own turn of the object pass. Six DOSBox-X runs (three more failed to compile on a
  declared `TARGET MOVE/BULLET/VERTEX`); implemented in `WdlRuntime`.

## Save files (2026-10-29 — SAVE_INFO confirmed and implemented; full SAVE partly read)

- `OV-20261029-save-format.md` — a save is the WRS entry framing (big-endian sizes, LZSS) around a
  little-endian body of self-locating sections; skill records are name, raw value, raw min, raw max.
  OpenVirtue writes `SAVE_INFO` files byte-identical to the original's, `LOAD_INFO` restores the
  `GLOBAL` and `LOCAL` skills at once, and a full original `SAVE` yields its `GLOBAL`/`PLAYER` skills
  and player position. Seven DOSBox-X runs; implemented in `AcknexSaveFile` and `WdlRuntime`. Addendum 2026-11-03: `SEEN` is set in every frame a
  wall is on the screen (the engine tests unseen walls each frame, budgeted).

## Sky (2026-10-29 — confirmed and implemented)

- `OV-20261029-sky.md` — the sky's sides spread over the full turn at `SCREEN_WIDTH/PLAYER_ARC`
  pixels per radian, half a field of view off the world (the right edge shows the view angle),
  `SKY_OFFS_X` moving it right; vertically unscaled, its last row on the tilted eye line, `SKY_OFFS_Y`
  moving it down, edge rows repeated. Fourteen DOSBox-X runs (ten failed to compile in round 1);
  implemented in `SkyPanorama`.

## Shading (2026-10-29 — confirmed and implemented; the AUTORANGE table in OV-20261029-autorange)

- `OV-20261029-shading.md` — the shade level is `30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d`,
  rounded down to even and clamped to 0…30 (16 grades over a 32-colour range): inverse-distance
  falloff, `AMBIENT` 0.5 brightening by 16 levels, `PLAYER_LIGHT` 0 giving level 30. Fifteen DOSBox-X
  runs; implemented in `PaletteShading` and the GL backend's indexed path.

## Slopes (2026-10-29 — confirmed and implemented)

- `OV-20261029-slope.md` — vertex heights tilt a region only with its slope flags: `FLOOR_ASCEND`
  and `FLOOR_LIFTED` raise the floor by them, `FLOOR_DESCEND` lowers it, `CEIL_ASCEND` raises the
  ceiling, and `CEIL_DESCEND` and `CEIL_LIFTED` lower it; without a flag the surface is flat. Sixteen
  DOSBox-X runs (eight failed to compile in round 1); implemented in `LevelTopology.SlopeSign`.

## Sound channels (2026-10-29 — confirmed and implemented)

- `OV-20261029-audio.md` — channel allocation and timing as scripts see them:
  - a sound takes the lowest free channel, or else the oldest normal sound's channel;
  - `SLOOP` channels (2) are never taken;
  - inaudible sounds are skipped and leave `CHANNEL` alone;
  - a channel frees at the first world cycle after its sound ends, and not during a flic;
  - `STOP_SOUND` frees the normal channels two cycles later and leaves loops playing. Addendum 2026-11-04: a world frame is capped at 8
  ticks (`Timing.MaxFrameSeconds` 0.5); flic cycles keep their delay.

  Eighteen DOSBox-X runs; implemented in `WdlRuntime`.

## Animation phase timing (2026-10-29 — confirmed and implemented)

- `OV-20261029-anim-timing.md` — the phase lengths of a texture animation loop are fixed in frames
  when the loop starts: ⌊DELAY·(1 + RANDOM·u) / TIME_CORR⌋, with that cycle's `TIME_CORR` and one random
  draw per loop. A `ONESHOT` plays with the step fixed when its last run (or the level) started. A
  re-analysis of the motion-timing traces; implemented in `WdlRuntime.AdvanceAnimationTarget`.

## Shade tables (2026-10-29 — confirmed and implemented)

- `OV-20261029-autorange.md` — the colour drawn for each palette index and shade level:
  - a sorted range moves place i of n by `⌊(n + 1 − i)·L/32⌋`, past the end to `INFINITY`;
  - `AUTORANGE` fades each colour toward `INFINITY` in whole 6-bit steps, `(c·(32−L) + I·L) >> 5`,
    then takes the nearest range colour.

  Thirty-two DOSBox-X screenshots, every cell reproduced; implemented in `PaletteShading.BuildTable`.

## Full-save objects (2026-10-29 — confirmed and implemented for reading)

- `OV-20261029-save-body.md` — a full `SAVE`'s fourth section holds 170-byte thing/actor records and
  178-byte wall records in reverse map order, with fixed offsets for position, angle, speeds, `DIST`,
  `CYCLE`, targets, offsets and skills; regions are not saved; the 672-byte section holds the action
  slots. Original saves now load object state too. Addendum 2026-11-03: in a sloped region
  `WDIST_MINX/MAXX/MINY/MAXY` are the axis lines' nearest wall crossings through the player (89 of 107). Round 21: `WDIST_*` are distances from the player.

## BELOW stacks in the view test (2026-10-29 — confirmed and implemented)

- `OV-20261029-below-view.md` — a sight line passes walls only through openings between storeys and
  keeps to one storey across a stack's footprint, so the underpass shows through the opening beside
  it and the slab hides it from above. Four DOSBox-X runs; `VISIBLE` matches in every view.

## Actor steps along ANGLE (2026-10-29 — confirmed and implemented)

- `OV-20261029-actor-step.md` — a `MOVE`/`BULLET` step is `SPEED·TIME_CORR` in 22.10, split into
  `round(step·cos θ)` and `round(step·sin θ)` raw units. cos and sin are first cut toward zero to
  22.10, which the long out-of-view steps show. 20 of 25 carriers replay line for line. Addendum
  2026-11-03: an actor in rock is held off no wall; a wall hold keeps the step's part along the
  wall (33 of 36).

## INKEY editing (2026-10-29 — confirmed and implemented)

- `OV-20261029-inkey.md` — the input edits the old content with the cursor at its start, inserting
  and cutting to the declared length (the last character is replaced at the end); BackSpace deletes
  before the cursor; the world keeps running. Eleven DOSBox-X runs with scripted keys. Addendum
  2026-11-03: a completed `INKEY` resumes its action in the next cycle's resume slot, like a
  `WAIT`; the carriers replay (10 of 13, the rest failed runs). Round 3: [Del] and [Left] as modelled; `STR_LEN` without the pad (12 of 15).

## DIGITS and TEXT layout (2026-10-29 — confirmed and implemented)

- `OV-20261029-ui-layout.md` — `DIGITS` rounds the value and right-aligns it with drawn spaces, keeps
  the last `len` digits and draws a digit font's missing minus as a space; `CONDENSED`/`NARROW`
  shorten only the advance. Nine DOSBox-X screenshots.

## Panel widgets and the pointer (2026-10-29 — confirmed and implemented)

- `OV-20261029-ui-widgets.md`:
  - bars show from `trunc(fac·skill)` and wrap past their range;
  - `WINDOW` Y counts from the bitmap's bottom and X stops one pixel short;
  - `PICTURE` 0 is side 1;
  - the pointer is drawn at `MOUSE_X`/`MOUSE_Y`.

  Five DOSBox-X screenshots.

## Multi-line TEXT (2026-10-29 — confirmed and implemented)

- `OV-20261029-ui-text.md` — lines are drawn whole or not at all within `POS_Y…POS_Y+SIZE_Y`
  after `OFFSET_Y`; `CENTER_Y` centres the first line, `CENTER_X` each line. Six DOSBox-X screenshots.

## Overlapping the player start (2026-10-29 — implemented)

- `OV-20261029-overlap.md` — at start-up or after a beam, the player is pushed out of one object
  only: the first overlapping one in reverse map order, and not at all on its exact centre.
  - The push goes along the centre line to √(c² + 0.093), the player's measured contact distance
    with objects in general.
  - A zero component gets the 4/1024 nudge.
  - Off-axis residuals of up to 0.008 are open. Settled by the 22.10 push-out of
    OV-20261103-startup-clock, but for one unit on 4-wide objects.
  - Walking into an object (2026-11-03 addendum, still-walk): the step, its parts raised to
    4/1024, is taken in full; when it ends with L² < (c − 1)²/1024 in 22.10 it is pushed back to
    C² as at start-up, and the push-back does not count in `PLAYER_VX`/`PLAYER_VY`.

## The start-up frames and push-out (2026-11-03 — confirmed and implemented)

- `OV-20261103-startup-clock.md` — four identical launches and 13 more carriers sampled every cycle
  from `IF_START`:
  - Cycle 1 reads `TICKS` 0. From cycle 2 the run's 60 Hz 8/9 cadence holds, and cycle 1 is its
    earliest frame past the clock's start, so the step into cycle 2 is 9 to 17 thirty-seconds. The
    replay plans the start-up from the first samples' cadence run backward.
  - The start-up push-out comes in cycle 2. It is done in 22.10: squared lengths cut, C² = c²/1024
    + 98, the offset scaled by the root of the cut ratio. It reaches no object 8 steps or more away.
  - An addendum covers the replay's choice of timing column when an event token is denser than the
    sampler (border-climb).
  - A push whose root argument overflows 32 bits (0.05 from a 2-wide actor's centre) moves the
    player `PLAYER_WIDTH`/4 and finishes the cycle after (2026-11-03 addendum, push-d005).
  - The contact term is C² = (c − 1)²/1024 + 102 for every width measured, 1 to 16 wide (2026-11-03
    width addendum); the two 4-wide pushes off the axes now land on the unit.

## Contact steps, the seconds clock, a skipped follower (2026-11-03 — confirmed and implemented)

- `OV-20261103-contact-clock.md` — ten carriers at 60 frames a second and two at 8 ticks a cycle:
  - a player step meets things and actors once, at its end, nudge included, and is pushed back in
    22.10: C² = c²/1024 + 98 for a thing, (c − 1)²/1024 + 102 for an actor;
  - `SECS` and `EACH_SEC` run on real time, which `TICKS`, summed from 8/32- and 9/32-tick frames,
    gains 0.9% on at 60 frames a second (the replay: 0.991 and a 1.28-tick start);
  - `TIME_TOTAL` is 64 × `TIME_CORR`;
  - a skipped actor moves in cycles 6, 12, … by six of that cycle's steps.

  The 3.2 step and a few one-unit pushes are open.

## The refused long step, the standing hold, a wall's DISTANCE after a beam (2026-11-03 — confirmed and implemented)

- `OV-20261103-careful-stop.md` — seven carriers at 60 frames a second:
  - a skipped `CAREFULLY` actor's long step is tried whole, half, quarter and eighth: a clear end
    is taken, one within the half width before a border goes to the 1074-unit hold down to the
    quarter, and at the eighth an end not clear moves half the way to the hold;
  - an actor nearer than its half width to a solid wall is put at its hold in place of its move,
    with the standing arrival still counted; a step that would reach its way point arrives though
    a wall stops it short;
  - a wall's, a thing's and an actor's `DISTANCE` after the player's own move without a turn (a
    beam or a walk) is the point's |depth| + |lateral| in the player's view (22.10 trig); a turn,
    an actor's front move, the eye height and `PLAYER_ARC` give the map's |dx| + |dy| (round 3);
  - a sprite shows while a full pixel column of it is on the screen (round 3: 2- and 4-wide actors
    at 40 and 20 lost at 0.521 and 0.543), as the measured bottom row.

  A wall end's column rounding and unit residuals of the view's value are left as read.
- `OV-20261104-instructions.md` — eighteen carriers on the instructions no earlier probe
  measured: `SHIFT`, `ROTATE` and `LIFT` move a region's vertices, things and actors (not the
  player on it) once, and the region takes no other until one of its walls or objects is
  `SHAKE`n; `LIFT` raises only the vertices; `ROTATE` leaves `ANGLE`s (16.16 trig, floored
  products); `SHAKE` puts a thing on its floor and refreshes its `DISTANCE`; `DROP` goes along
  the cycle's starting angle with 1/1024 sines into the region of its point; `SET_ALL` reaches
  every placement and `SET` the first, and `SET_ALL` on a position retires the objects; an
  `INVISIBLE` thing runs no `EACH_TICK`; `PUSH n` fires the nearest object the last frame drew
  within `n`, a wall at its `DISTANCE`; `EXPLODE` fires the region's `FRAGILE` objects from the
  last placed by the distance to their nearest edge; `RESULT` is 1 − the share cut to 1/1024; a
  free-standing solid wall stops a `SHOOT`, whose `HIT_X` on a wall is the texel from its left end
  as seen, a unit under (round 4; oblique wall hits are a little off, open). Implemented.
- `OV-20261104-move-mode.md` — `MOVE_MODE` 0.5 takes the regions, actors, things and walls out of
  the cycle (their `EACH_TICK`s, the actors' steps) from the cycle that sets it; 0 stops the
  player's movement and the object and region events too; −0.5 stops everything but `TICKS` and
  the key hooks (no resumed wait, no global slot, no `EACH_SEC`, its clock frozen). Four
  carriers; implemented.
- `OV-20261104-strings.md` — `TO_STRING` writes a whole 22.10 value without decimals and any
  other with three, rounded as `PRINT_VALUE`; a `SET_STRING`, `ADD_STRING` or `TO_STRING` copy
  takes one character more than the string's declared length (`INKEY` stops at the length);
  `SET_SKILL` reads the number a text starts with (blanks, sign, decimals, exponent), cut to
  22.10, and 0 for none. Three carriers; implemented.
- `OV-20261104-level.md` — `LEVEL` lets the action go on to the cycle's end, then the new world
  runs its `IF_START` with `TICKS` and `TOTAL_FRAMES` started over, `GLOBAL` skills kept, plain
  skills, waiting actions and the player's position started over. One carrier; implemented.
