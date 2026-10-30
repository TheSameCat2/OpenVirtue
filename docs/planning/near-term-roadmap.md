# Near-Term Roadmap

This is the current practical plan after the first runtime/player integration.
It complements the larger milestone table in
[`docs/recon/06-reimplementation-strategy.md`](../recon/06-reimplementation-strategy.md).

## Track 1: Finish The Easy Part

"Easy" means work we can do from our own code, public docs, user-supplied data
files, guarded tests, and already-observed behavior without needing deep hidden
engine invariants.

1. Stabilize the viewer as a development harness.
   - Show live debug state: position, region, floor/ceiling, FPS, `TIME_CORR`,
     startup action status, and loaded world.
   - Keep movement status explicit while WDL-driven physics approaches parity.
   - Make all six retail archives load through `ovtool level info` and the app
     where possible. Guarded local tests now cover all six archives through level
     load, mesh generation, texture decode, and runtime boot/tick. A local Apathy
     interactive smoke now verifies startup, input, rendering, and a stable
     roughly 60 FPS after moving animated world phase selection to draw time.
     The app now keeps the authored WMP `PLAYER_START` instead of replacing it
     with a debug-room spawn, and the same smoke confirms Apathy starts in its
     declared amphitheater region. The same smoke now visibly renders the
     authored amphitheater, instruction text, sprites, and HUD after binding
     frame constants to the pixel shader; previously the unbound pixel-stage
     buffer multiplied every sampled texture by zero.
2. Finish low-risk renderer/runtime foundation.
   - Sky/background handling (WDL `FLAGS SKY` textures are excluded from
     perspective geometry and rendered as a camera-selected background side
     with `SKY_OFFS_X/Y`; exact panorama stitching and vertical behavior remain).
   - First-pass palette/lighting approximation now combines global, texture,
     and per-region ambient inputs with `PLAYER_LIGHT` in the pixel shader.
     Palette fades preserve color cast without treating average palette
     luminance as exposure; true indexed-palette shading remains.
   - Two-sided WMP walls now render as portals: only floor and ceiling height
     differences emit textured wall segments, while matching openings remain
     visible between regions.
   - Wall material batches preserve palette-index-0 alpha and enable cutout only
     for `FLAGS TRANSPARENT`, so an opaque wall can safely reuse the same texture.
   - The 3-D projection is measured, not assumed (OV-20261009-projection-fov-player-arc,
     -pixel-aspect-video-modes, -tilt-model-shear-vs-pitch; 19 DOSBox-X runs of the
     patched DOS runtime on synthetic rooms): horizontal FOV = `PLAYER_ARC` radians,
     vertical scale 0.75·`SCREEN_HGT`/(`ASPECT`·tan(arc/2)) with no pixel-aspect
     pre-stretch, `PLAYER_TILT` a y-shear of −tilt·`SCREEN_HGT` rows (positive looks
     down). `OpenVirtue.Engine.Rendering.ProjectionModel` holds it; the D3D11 view
     is yaw-only and the GL renderer (ADR-0008) must consume the same model. The world
     handedness read off the same screenshots (map +y on the screen left when facing
     +x) is implemented by ADR-0009: render z = −map y and camera yaw =
     `PLAYER_ANGLE` + π/2 in one `RenderSpace` helper, the WMP start angle seeds
     `PLAYER_ANGLE` (0 = +x), and engine tests push mesh/sprite output through the
     look-at and the projection matrix to pin the side; the hitscan's lateral sign was
     already right.
     Visual confirmation of that frame was done on the GL host on 2026-10-15
     (`Tilt0FrameTests`, stripe edges within 1 px of `OVTLT0.PCX`, floor at the bottom).
     The hitscan is measured as well
     (OV-20261009-shoot-mapping, 2026-10-10): `SHOOT_X`/`SHOOT_Y` share the projection's
     tangents with positive `SHOOT_Y` aiming up, the tilt shear acts on the ray, `HIT_DIST`
     is the depth along the line of sight, thing bitmaps are unmirrored and a wall's
     bitmap starts at its second vertex seen from the left region. Remaining probes: sky
     offset under tilt, windows smaller than the mode, tilt beyond ±1, wall texture seen
     from the right region and the `OFFSET_X` sense, `SHOOT` at other arcs/aspects.
   - Sprite visibility/sorting fixes as needed for static scenes (static billboard
     layout is engine-side, interpolates sloped region floors for entity feet and
     shadows, and app rendering orders billboards back-to-front).
   - Runtime object registration for globals, placed things/actors, and player.
   - Object/region/wall `EACH_TICK` assignments are live runtime fields, so
     `SET my.EACH_TICK, action` and `SET my.EACH_TICK, NULL` affect later
     frames. Texture `SIDES`, `CYCLES`, `DELAY`, ordered `BMAPS` phases, `NULL`
     phases, and `ONESHOT` are preserved. Runtime animation advances the current
     `CYCLE` by documented phase delays, leaves unrelated `POSITION` state alone,
     clears `PLAY` at the end of an `ONESHOT`, and dispatches `EACH_CYCLE` when
     an animation wraps or finishes. Sprite and world rendering select the live
     bitmap phase; sprites also select directional sides from the viewer angle,
     apply per-side mirroring, and shift individual frames by their declared
     pixel offsets. Animated wall/region batches retain their owner and select
     the owner's live phase at draw time, so immutable geometry and GPU textures
     are not rebuilt every tick. Random delay jitter, camera-dependent wall sides, and exact
     old-engine dispatch order remain parity work.
3. Convert assumptions into tests.
   - Prefer synthetic WDL/WMP fixtures.
   - Keep real-data tests guarded and local-only.
   - Run the CI-shaped build/test pair before merging.

## Track 1A: Retail WDL Runtime Coverage

Before adding more gameplay systems, use sanitized aggregate coverage to decide
which WDL runtime primitives matter most for the retail scripts. The tool is:

```bash
dotnet run --project tools/OpenVirtue.Tools -- wdl coverage <archive-or-dir> [main.wdl]
```

When given a directory, it discovers `*.wrs` archives recursively and defaults
each main script name from the archive name (`apathy.wrs` -> `APATHY.WDL`).
The report is intentionally clean-room safe: it prints only archive/main-WDL
names plus aggregate keyword/count tables. It does not emit WDL source lines,
literal strings, action bodies, or proprietary asset/script content.
Nested action-body `IFDEF`/`IFNDEF`/`IFELSE`/`ENDIF` preprocessor branches are
flattened before coverage counts, so disabled branches do not look like runtime
interpreter gaps. A standalone `ENDIF` outside such a preprocessor branch still
counts as the legacy runtime terminator for `IF_*` instruction blocks.

Use the generated tables to prioritize:

- Declaration/item keywords that are common but not yet indexed by loaders.
- ACTION-body statement keywords not implemented as built-in interpreter
  instructions.
- `IF_*` hook declarations and runtime reassignments that need dispatch
  semantics.
- `EACH_TICK`/`EACH_CYCLE` declarations and reassignments that need scheduler
  parity.
- References to predefined runtime timing skills currently maintained by the
  runtime (`TIME_CORR`, `TIME_FAC`, `TICKS`, `SECS`).

Do not commit retail coverage output if it contains anything beyond sanitized
aggregate counts. If a count points at undocumented behavior, follow the oracle
protocol before implementing hidden runtime details.

An initial local pass over the user-supplied retail archives points to these
near-term interpreter/runtime targets, in order:

Latest sanitized local coverage shows no remaining ACTION-body statement
keywords outside the interpreter's built-in set. The remaining work is behavior
depth: exact dispatch/order semantics, host integrations, movement/collision,
and old-engine edge cases.

- Common UI declaration keywords (`OVERLAY`, `OVLY`, `PANEL`, `TEXT`, `FONT`,
  `VIDEO`, and related top-level items) are now preserved as structural
  `Level.UiElements` data. Top-level `BMAP`/`OVLY` resources are exposed through
  `Level.Bitmaps`, and the app renders first-pass visible bitmap overlays plus
  visible panel backgrounds as screen-space quads. Visible `TEXT` declarations
  now render live WDL `STRING` contents through declared `FONT` PCX atlases.
  Visible panels/overlays with `IF_KLICK` now intercept left clicks before the
  global click hook. First-pass panel `DIGITS` displays render live skill-backed
  numeric values through digit/ASCII font atlases. First-pass panel `WINDOW`
  displays render live skill-positioned bitmap cutouts. First-pass panel
  `PICTURE` displays render skill-selected texture frames. Panel `BUTTON` widgets now render
  their inactive bitmap, switch to the active bitmap while held, dispatch their
  press and release actions, and support `NULL` inactive bitmaps. Panel
  `HSLIDER`/`VSLIDER` widgets render their live skill position and update the
  bounded skill while dragged. Panel `HBAR`/`VBAR` displays render live
  skill-shifted bitmap cutouts. Text rendering now handles documented
  `CENTER_X`/`CENTER_Y`, `CONDENSED`/`NARROW`, and first-pass vertical
  `SIZE_Y`/`OFFSET_Y` clipping. Active top-level `VIDEO` modes are preserved as
  the level's virtual pixel grid; the app letterboxes that grid to the mode's
  4:3 display aspect (so 320x200 and X320x400 frames are stretched the way a
  VGA monitor showed them, per OV-20261009-pixel-aspect-video-modes) and maps the
  live `SCREEN_X`/`SCREEN_Y`/`SCREEN_WIDTH`/`SCREEN_HGT` 3D viewport, UI rendering,
  mouse skills, hit testing, and slider dragging through the same transform.
  Runtime `SET`/`RULE` mutations for common UI numeric fields and flags such as
  `POS_X`, `POS_Y`, and `VISIBLE` now feed rendering and hit testing. Runtime
  keyword retargeting for UI members such as `TEXT.STRING`, `FONT`, `BMAP`, and
  `PAN_MAP` also feeds the live UI builders, so menu/HUD scripts can swap visible
  text and bitmaps after startup. `TEXT.STRING` retargeting now respects the
  current `TEXT.INDEX` slot for multi-string texts.
- Legacy conditional instructions: `IF_EQUAL`, `IF_BELOW`, `IF_ABOVE`,
  and `IF_NEQUAL` now execute as flat `ELSE`/`ENDIF` blocks. `IF_MIN`
  still needs stronger documentation or an oracle note before implementation.
- Arithmetic/control-flow instructions that appear outside the current
  structured subset: `WAIT` and `WAITT` now suspend and resume actions through
  the runtime tick queue, including loops, called actions, and object `my`/`there`
  scope. Exact dispatch ordering and hidden edge cases still need oracle
  calibration. `ACCEL` now applies the public manual's documented
  `TIME_CORR`/`FRICTION`/`INERTIA` velocity update; old-engine creep
  suppression and oscillation edge cases still need oracle calibration. `ADD`
  now increments numeric skill/object-member targets, and `ADDT` applies the
  same increment scaled by `TIME_CORR`. `BRANCH` is a tail jump measured against
  the DOS runtime (OV-20261015-branch-tail-jump, eleven runs): the target runs in place
  of the current action, nothing after the `BRANCH` runs (not even the rest of an enclosing
  `IF`/`WHILE`), the branching action's caller resumes as it would for the action itself,
  chains do not nest, and a `NULL` synonym target is skipped. `SKIP` now advances past a numeric
  count of following statements. `IF_MIN` now tests whether a bounded skill is
  currently at its declared `MIN`; exact old-engine comparison tolerance remains
  parity work.
- Sound/palette/input/file instructions that need either implementation or
  clean no-op policy: `PLAY_SOUND`, `PLAY_SOUNDFILE`, and `STOP_SOUND` now
  produce host-level runtime sound requests. The runtime reserves the documented
  eight channels synchronously, exposes `CHANNEL`/`CHANNEL_0..7`, applies the
  documented `SOUND_VOL` default, and releases channels on host completion or
  `STOP_SOUND`. The host resolves WAV data from the current WRS archive,
  supports overlapping voices and numeric balance, and applies first-pass
  object-relative pan plus linear `SDIST`/`SVDIST` attenuation. Texture `SOUND`,
  `SVOL`, `SCYCLES`, and `SLOOP` are retained; grounded movement now triggers floor
  sounds using the documented walk/wave periods and region entry starts one-shot or
  looping ceiling ambience through the same channel allocator. `SLOOP` wall, thing,
  and actor textures now start within earshot, stop outside it, and continuously
  update volume and stereo direction. Animated wall/entity textures emit sounds on
  the authored `SCYCLES` phases, with distance filtering before channel reservation.
  Exact attenuation, cadence and `WALK`/`WAVE` phase calibration, `PSOUND_TONE`,
  and channel priority remain parity work.
  `FADE_PAL` now records host-level palette fade requests, and
  the app applies a first-pass global tint from the requested WRS palette PCX;
  actual palette-table/shading/blur behavior remains. `LOCATE` now restores a
  thing/actor/player region from the loaded map geometry. `SHAKE` now records a
  geometry invalidation request after script-driven movement; `DROP`, `SHIFT`,
  `LIFT`, and `ROTATE` now emit the same invalidation request after mutating
  object or sector geometry, and the app rebuilds render meshes/sprites when
  new invalidations appear. `DROP` now places a thing/actor along the player's
  sight line at its `DIST` inside the player's current region. `SHIFT` now moves a region footprint plus contained
  things/actors/player on the map plane.
  `LIFT` now raises a region's vertex heights plus contained things/actors, and
  renderer surfaces/walls/sprites consume those Z offsets. `ROTATE` now rotates
  a region footprint plus contained things/actors/player around the region's
  documented `GENIUS` object. The documented `RANDOM(x)` expression function is
  available. `BEEP`, `PLAY_FLICFILE`,
  `STOP_FLIC`, and `SCREENSHOT` now record host-level presentation requests;
  the app services `BEEP`, plays 8-bit FLI/FLC requests full-screen, consumes
  flic stop requests, and writes first-pass PNG screenshots from the D3D
  backbuffer. Exact PC-speaker and legacy screenshot-format behavior remains. `INKEY` now
  records host-level text input requests, suspends the current action until the
  host completes the request, then updates the documented `RESULT` and
  `STR_LEN` skills; the app services pending requests with a first-pass modal
  text prompt so suspended scripts can resume. `RANDOMIZE` now reseeds the runtime random-number generator;
  exact legacy RNG sequence remains parity work. `SAVE`,
  `LOAD`, `SAVE_INFO`, and
  `LOAD_INFO` now record host-level persistence requests with the documented
  five-character base name and numeric slot; the app services first-pass
  `SAVE`/`LOAD` by writing and restoring OpenVirtue-local JSON runtime snapshots
  under `openvirtue-saves` beside the user's archive. Actual legacy `.SAV`
  serialization remains. The app also supplies a runtime print directory, so
  retail `PRINTFILE`/`PRINT_VALUE`/`PRINT_STRING` output lands under
  `openvirtue-prints` beside the user's archive.
  `PLAY_FLICFILE` records an identified host request and its action goes on at
  once, as measured in OV-20261015-flic-play-blocking (issue #9): during the flic
  the global `EACH_TICK` and `EACH_SEC` slots and `WAITT` actions keep running and
  key hooks fire, `STOP_FLIC` ends the flic before the next frame, and
  `FLIC_FRAME` is the 1-based frame on screen (1 right after the instruction) and 0
  when no flic plays. A `STOP_FLIC` and a later `PLAY_FLICFILE` in one frame are
  applied in script order. The clean-room FLI/FLC reader
  decodes palette, copy, black, byte-run, line-compressed, and word-delta chunks;
  the app presents decoded frames at their authored delay,
  pauses world/object simulation, and ignores
  the optional trailing ring frame. Unsupported or invalid files play
  nothing. The exact `WAITT` rate during a flic, object scheduler behavior during a
  flic, and audio synchronization remain oracle-calibration work.
  `SHOOT` and `PUSH` now perform first-pass 2D player/object line-of-sight
  interactions, update documented `HIT_DIST`, `RESULT`, and `SHOOT_ANGLE`
  skills, and dispatch `IF_HIT` for player shots. Hitscan and actor visibility
  traverse two-sided portal openings at eye height while lower/upper wall spans
  remain solid; synthetic tests cover open and height-blocked portals, and a
  guarded Apathy test executes its real `IF_LEFT` weapon binding. Player shots
  now resolve the live animation frame and directional side, apply frame offsets
  and mirroring, continue through transparent sprite pixels, and report sprite
  `HIT_X`/`HIT_Y` coordinates. Wall hits report texture pixels from the rendered
  wall orientation, including WDL offsets and sloped lower/upper portal spans.
  Normalized `SHOOT_X`/`SHOOT_Y` offsets now produce a projected 3D ray through
  the measured `ProjectionModel` (the same `PLAYER_ARC`, `ASPECT`, `PLAYER_TILT`
  and 3-D window size the renderer uses);
  `PUSH` remains centered as a separate interaction. Exact old-engine
  collision/sector details remain parity work. Level loading now preserves
  whether each skill declaration explicitly supplied `VAL`; runtime initialization
  can therefore apply documented predefined-skill defaults (including
  `PLAYER_ARC=1`) to bare system declarations without overriding authored zeroes.
  `EXPLODE` now triggers `IF_HIT` on nearby `FRAGILE`
  objects/walls, reports `HIT_MINDIST`, and updates player hit skills when the
  player is in range; exact nearest-edge/collision propagation and chain-reaction
  ordering remain parity work.
  `EXIT`, `LEVEL`, and `MAP` now produce host-level runtime requests; the app
  closes on `EXIT`, reloads requested WDL levels, and reloads the current WDL
  against requested WMP maps. Cross-archive `LEVEL` requests resolve against
  WRS files beside the current archive. Following the published A3 specification,
  `LEVEL` now carries forward only skills declared `TYPE GLOBAL` by both worlds,
  applies those values over the destination's `VAL` initializers, and then runs
  the destination `IF_START`; objects, scheduler actions, hooks, strings, and
  normal skills are rebuilt from the destination WDL. A guarded retail-data test
  confirms that the four gameplay worlds use a matching global declaration set.
  An end-to-end natural gameplay transition still needs validation, and `MAP`
  preservation semantics plus complete score compatibility remain parity work.
- Scheduler and hook reassignment semantics: global `EACH_TICK` slots, the 16
  global `EACH_SEC` slots (issue #8, OV-20261015-each-sec-cadence:
  `SET EACH_SEC.n, action|NULL` and top-level `EACH_SEC a, b;`; all slots share the
  runtime's second, 16 ticks of the same capped frame time as `TICKS`, and run in the
  frame where the whole-second `SECS` count goes up, just before the global `EACH_TICK`
  slots; a waiting slot action keeps going while the next second starts another; slots
  reset on level change and are part of the harness snapshot; they also run during a
  FLIC, OV-20261015-flic-play-blocking),
  object/region/wall `EACH_TICK` fields, object/region/wall `EACH_CYCLE`
  fields, and frequent `IF_*` runtime `SET` targets now update live runtime
  state. The app dispatches first-pass global keyboard/mouse hooks for
  `IF_ANYKEY`, function keys, letters, digits, common control/navigation keys,
  punctuation hooks used by the retail scripts, and mouse
  `IF_LEFT`/`IF_RIGHT`/`IF_MIDDLE` plus first-pass global `IF_KLICK`;
  documented keyboard/mouse polling skills and keyboard-derived
  `FORCE_AHEAD`/`FORCE_STRAFE`/`FORCE_ROT`/`FORCE_TILT`/`FORCE_UP` values are
  live before those hooks execute. The app now consumes script-authored
  `PLAYER_VX`/`PLAYER_VY`/`PLAYER_VZ` and `PLAYER_VROT` each frame with
  `TIME_CORR`, routes motion through portal and floor/ceiling collision, adapts
  blocked velocities, and
  updates `IMPACT_*`, `PLAYER_LAST_*`, `PLAYER_SPEED`, `ACCELERATION`, and
  direction/height skills. Horizontal collision maintains live `PLAYER_WIDTH`,
  slides along solid walls, honors runtime `PASSABLE` changes, and collides with
  things/actors using documented texture scaling plus `THING_WIDTH`/`ACTOR_WIDTH`.
  Documented `BELOW` links now drive first-pass permeable shared surfaces when
  the upper/lower `IF_DIVE`/`IF_ARISE` hooks are paired; transitions update
  `HERE`/`PLAYER_DEPTH` and dispatch the vertical hook without horizontal
  enter/leave events. Player floor and ceiling queries now barycentrically
  interpolate WMP vertex Z over the same ear-clipped triangles used by rendering;
  spawn, vertical collision, `FLOOR_HGT`/`CEIL_HGT`, and directional
  `SLOPE_AHEAD`/`SLOPE_SIDE` therefore follow authored ramps. Exact old-engine
  collision ordering, non-planar surface edges, object-height overlap, and
  multi-level transition edge cases remain parity work.
  Things/actors now preserve object-level `IF_KLICK` assignments, and the app
  performs first-pass screen-space billboard picking to dispatch them before
  falling back to the global click hook. It also bridges the live
  debug-player position/region/facing with WDL-visible `PLAYER_*` skills before
  startup scripts, input hooks, and frame ticks, then applies script-authored
  `PLAYER_*` movement/angle changes back to the visible app player. `Tick` now advances
  A2-style legacy comparisons without an `ENDIF` as single-instruction
  conditionals, preserving label/`GOTO` state machines while retaining the
  block form when an `ELSE`/`ENDIF` is present. This unblocks Apathy's installed
  retail movement action: a guarded integration test runs its real startup and
  verifies that sustained `FORCE_AHEAD` produces finite, collision-aware player
  displacement and a valid region. Exact movement constants and feel remain
  parity work. `Tick` also advances
  object/wall/region texture phases using declared tick delays and dispatches
  `EACH_CYCLE` when an animation wraps or a played one-shot finishes. Exact
  dispatch ordering, random delay variation, and event detector conditions remain
  parity work.
  Player region changes observed during
  `Tick` now update `HERE` and dispatch first-pass region `IF_LEAVE`/`IF_ENTER`
  hooks once per transition. Player proximity now dispatches first-pass
  object/wall `IF_NEAR` and `IF_FAR` hooks on `DIST` threshold crossings in the
  player's current region; portal collision thresholds, proximity edge cases,
  and exact old-engine event ordering remain parity work.
  Actor `TARGET`/`WAY`/`OVERLAY`/`ATTACH` keyword members now survive level load,
  runtime `SET`, and snapshots. Thing/actor `ATTACH` textures now render as
  independent animated/directional layers; `BEHIND` controls front/back depth,
  and `SHADOW` attachments render as color-keyed floor quads. Apathy's 14
  authored entity attachments are all shadow layers and participate in the
  retail smoke. Sprite click testing selects the frontmost upright layer,
  ignores `IMMATERIAL` entities and shadow quads, and rejects transparent
  pixels using the live animation frame's alpha mask. Each runtime tick advances documented
  non-`CAREFULLY` `MOVE`/`BULLET`, `FOLLOW`/`REPEL`, `VERTEX`, and named-object
  targets with `TIME_CORR`, live target coordinates, region tracking, `MOVED`,
  turning, documented `VSPEED` height adjustment, and edge-triggered `IF_ARRIVED`
  after both horizontal and vertical arrival; the app refreshes billboard positions
  after movement. Entity `HEIGHT` now aliases the rendered feet offset, honors the
  `GROUND` absolute-height convention, and is converted to preserve world height
  when scripts toggle `GROUND`. `CAREFULLY` actors now share radius-aware wall/portal/entity
  collision with the player, publish actor impact/floor/ceiling skills before
  `EACH_TICK` (including interpolated WMP slope height), and trigger documented
  collision hooks. Shared collision keeps
  `PASSABLE` scoped to player traversal as documented, while `CAREFULLY` actors
  still avoid visible things and actors. `STICK` captures or honors
  its relative player angle/distance and reports `IMPACT_VROT`. Runtime
  `INVISIBLE` changes now immediately maintain the derived `VISIBLE` flag;
  invisible things/actors are omitted from sprites, collision, interaction,
  proximity, shooting, and explosions, while invisible actors also pause target
  movement, animation, scheduler callbacks, and suspended object actions until
  made visible again. WMP `WAY name x1 y1 ... xn yn` paths now survive format
  and level loading. Actors with an assigned way follow its points cyclically at
  `SPEED`, use the documented 1-based `WAYPOINT`, publish the next
  `TARGET_X`/`TARGET_Y`, ignore `VSPEED`, and dispatch `IF_ARRIVED` at every point.
  `CAREFULLY` actors also dispatch `IF_ARRIVED` whenever movement crosses a region
  border, independent of target mode. Exact ordering when border and target arrival
  occur in the same tick remains parity work.
  UI hit testing now derives clickable `TEXT` bounds from the same live glyph
  layout used for rendering, including runtime strings, centering, condensed
  advances, clipping, and layers. `MSPRITE` retains its singleton declaration
  without overwriting the referenced overlay, remains hidden at `MOUSE_MODE=0`,
  and uses live `MOUSE_X`/`MOUSE_Y` minus the overlay hotspot when enabled. The
  host now reports virtual-pixel `MICKEY_X/Y` movement once per frame, preserves
  script ownership of active cursor coordinates, routes cursor hit testing through
  those coordinates, tracks `MOUSE_MOVING` with `MOUSE_CALM`/`MOUSE_TIME`, dispatches
  `IF_MSTOP`, and suppresses the duplicate native cursor over the render surface.
  Mouse modes 0/1 now convert virtual-pixel deltas and `MOUSE_SENSE` into bounded
  `FORCE_ROT`/`FORCE_TILT`, so the retail movement action owns mouse-look inertia;
  mode 2 remains decoupled for menu cursor movement. The first-pass 16-dot analog
  scale remains oracle-calibration work.
  A rendered smoke on the previous D3D11 host verified the retail title
  FLC, title splash, menu, and keyboard handoff from `TITLE.WRS` to Apathy in one
  app process. The same smoke passed on the GL host on 2026-10-15
  (`tests/OpenVirtue.App.Tests/RetailSmokeTests`, guarded on `_research/` and a GL
  context; frames go to `ov-retail-smoke/` in the temp folder). It drives the real
  SDL/GL host with scripted keys: the logo FLC plays, the logo times out to the
  splash on its own (no key, 10–14 engine seconds after its countdown is armed), Space
  shows the menu with the arrow on New Game, and Enter loads Apathy, which draws the
  amphitheater, instruction text, sprites and HUD with the player in the declared
  start region (479 `amphiStage`). `START`, `TITLE`, `APATHY`, `HEART`, `LEGALISM`
  and `NEWAGE` each open directly and draw a frame (`START` hands off to the title).
  What failed or differs:
  - Reading a `PANEL`/`TEXT`/`OVERLAY` member in an expression (`menu1_txt.POS_Y`)
    returned 0, so the menu arrow sat at the top of the screen. Fixed: member
    expressions now read UI elements the way `SET` writes them.
  - New Game skipped the difficulty menu and started at difficulty 3, because `BRANCH`
    returned like `CALL` and `menu_exec`'s `IF_EQUAL` chain went on with the new
    `menu_pos`. Fixed (issue #7): `BRANCH` is a tail jump (OV-20261015-branch-tail-jump);
    the smoke now requires the difficulty menu after the first Enter and difficulty 3
    after the second.
  - `EACH_SEC` was not implemented (it is not in the 1998 manual either), so the
    title's 12 s logo and 4 s splash timeouts never fired; only a key advanced. Fixed
    (issue #8): the 16 global `EACH_SEC` slots run once per engine second as measured
    in OV-20261015-each-sec-cadence (shared second boundary, before `EACH_TICK`,
    `SECS` a whole-second count). The slots also run during a FLIC
    (OV-20261015-flic-play-blocking); the smoke now waits for the splash's 4 s timeout.
  - `PLAY_FLICFILE` held its action until the flic ended, so the logo music started
    after the animation, `IF_ANYKEY` was armed only after the logo FLC finished and a
    key during it did nothing. Fixed (issue #9): the original goes on past the
    instruction and a key stops the flic (OV-20261015-flic-play-blocking); the smoke
    presses Space while the logo plays and the splash comes up on the next frame.
  - Opened directly, `HEART`, `LEGALISM` and `NEWAGE` drew the world but no HUD,
    crosshair or weapon, while Apathy opened the same way drew all three. Cause: their
    start action shows the control panel and runs the game initialization from a
    three-second `EACH_SEC` countdown. Fixed with issue #8; all three draw the HUD,
    crosshair and weapon about 3 s after opening (checked on the GL host 2026-10-15),
    and a guarded engine test pins it.
  - `HEART` (about 10 FPS) and `LEGALISM` (about 19 FPS) ran well below Apathy (about
    33 FPS) on the development host on 2026-10-15. Fixed (issue #11). The renderer is
    hardware GL (Mesa 4.6 on an Intel Arc B390), not llvmpipe; the smoke now logs
    `GL_RENDERER`. A profile showed that the sprite rebuild cost most of each frame: for
    every thing and actor it traced the region's wall loop over all walls and ear-clipped
    the floor again, only to read the slope height. `LevelTopology` now keeps each
    region's loop and triangulation per level. The loop is fixed after load; the
    triangulation is checked against the live vertex x/y on every lookup and redone when
    `SHIFT` or `ROTATE` has moved one. The texture-sound scan also rules out emitters
    without a looping sound texture before placing them. The smoke logs each archive's
    mean frame time over frames 61–120 without asserting it. On 2026-10-19, main before
    the fix and after the fix (uncapped host frame rate, paced rate in brackets):

    | archive | before | after |
    |---|---|---|
    | APATHY | 16.7 ms, 60 FPS | 9–11 ms, 93–111 FPS (64) |
    | HEART | 36.8 ms, 27 FPS | 8.5 ms, 118 FPS (64) |
    | LEGALISM | 20.6 ms, 48 FPS | 11.3–11.6 ms, 86–89 FPS (63) |
    | NEWAGE | 16.8 ms, 59 FPS | 10.8–12.5 ms, 80–93 FPS (63) |

    `TIME_CORR` stays at about 0.25 in every archive. What is left: the WDL tick
    (about 3.5 ms in HEART), about 950 GL draw calls (about 2.5 ms; uploading the world
    once did not help, the cost is the call count) and texture sounds (under 1 ms).

## Track 2: Discover Hidden Invariants Cleanly

Once the easy harness/foundation work is stable, use
[`docs/clean-room/oracle-protocol.md`](../clean-room/oracle-protocol.md) for
behavior that only the old runtime can answer.

First targets:

1. WDL numeric edge cases.
2. Scheduler and action dispatch order.
3. Player movement units, angle conventions, jump/gravity constants, and portal
   collision thresholds.
4. Actor animation timing and AI hook triggers.
5. Palette lighting/shading behavior.
6. Save/load semantics, if retail-save compatibility remains a goal.

### Status (2026-10-30)

The notes are under `docs/clean-room/observations/`. `ovtool oracle summary` replays every carrier
against the original's traces; 218 of 478 match line for line. Most of the rest differ only in the
`TICKS` of input-timed lines or after a sampler stops.

| Target | Measured and implemented | Still open |
|---|---|---|
| 1. Numbers | 22.10 fixed point, wrap and saturation, `*` and `/` toward zero, `INT` down, `%` and bitwise on raw bits, function results (`numeric-edge`) | engine-written values (rounded or truncated) |
| 2. Scheduler | cycle order, start-up `TIME_CORR`, waits armed in `IF_START`, fractional `WAIT`s, same-cycle `SET EACH_TICK`, each kind and the regions from the last placed (`scheduler-order`, `flic-*`, `each-sec-cadence`) | waits armed by key hooks, `WAITT` below 1 |
| 3. Player movement | projection, tilt, steps and nudges, climbing, eye clamp, wall and object contact, push-out (`projection-*`, `tilt-*`, `border-*`, `eye-clamp`, `clip-*`, `actor-contact`, `overlap`) | the push direction off the axes (≤ 0.008) |
| 4. Actors | steps along `ANGLE` and toward points, `CAREFULLY`, way arrival, facing, events, view activity, animation phases (`actor-*`, `view-activity`, `anim-timing`, `motion-timing`) | one-unit step residuals after many cycles, `CAREFULLY` slides, the `RANDOM` distribution |
| 5. Shading | depth curve, `AMBIENT` (region, texture, negative), sorted ranges and `AUTORANGE` tables, sky (`shading`, `autorange`, `sky`) | `ALBEDO`/`GENIUS` light, `HARD` on unsorted palettes |
| 6. Save/load | `SAVE_INFO` both ways, reading full saves' skills and objects, how `LOAD` restores (deferred, waiting actions back, `LOCAL` kept), edited saves accepted, waiting actions and slots imported from the original's saves, full saves the original loads written (checked on one level) (`save-format`, `save-body`) | the writer on real levels, object actions' `MY`/`YOU`, `EACH_SEC` slots |

Also measured: slopes, `BELOW` storeys and their view test, sound channels, `INKEY`, panel
text, widgets and sliders, and shots (`HIT_X`/`HIT_Y`).

## Definition Of Done For This Phase

- `main` stays warning-clean under `dotnet build -warnaserror`.
- The app is a dependable level inspection harness, not just a renderer demo.
- Every newly discovered hidden invariant has an observation note and a test.
- The clean-room boundary is visible in docs, tests, and provenance notes.
