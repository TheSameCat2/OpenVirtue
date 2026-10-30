# OpenVirtue

[![CI](https://github.com/TheSameCat2/OpenVirtue/actions/workflows/ci.yml/badge.svg)](https://github.com/TheSameCat2/OpenVirtue/actions/workflows/ci.yml)

A clean-room, open-source reimplementation of the **Acknex-3 (3D GameStudio A3)**
engine runtime, with the goal of running the 1999 Christian FPS **Saints of
Virtue** natively on modern Linux and Windows — and, once parity is reached, improving its
graphics.

This project follows the **OpenMW / engine-remake model**: we ship *only* our own
code. The end user supplies their own legally obtained copy of the original game,
whose data files our engine reads at runtime. **No original game assets, scripts,
or executables are distributed with this project.**

> Status: **early implementation.** The asset pipeline (WRS/PCX/WDL/WMP/WAV) works
> and a textured level renders in an OpenGL 3.3 window with billboard sprites; the
> WDL interpreter boots/ticks in the viewer, and first-person movement has a first
> playable debug slice. The actual game simulation is still being built. See
> [Current status](#current-status) for the milestone breakdown, and
> [`docs/recon/`](docs/recon/) for the original research.

## The original game

- **Saints of Virtue** (1999) — developed by **Shine Studios**, published by
  **Cactus Game Design**. One of the first Christian first-person shooters.
- Built on **Conitec Datensysteme's Acknex-3 engine** (later marketed as *3D
  GameStudio*), a Doom-era 2.5D portal/sector engine scripted in the **WDL**
  language. Ships both a DOS runtime (`VRUN.EXE`) and a Windows/DirectX runtime
  (`WVRUN.EXE`).

## Our goals

1. **Parity first.** Faithfully reproduce Acknex-3 runtime behavior so the
   retail game plays correctly, end to end.
2. **Native & modern.** Written in **C#/.NET 10**, rendering through **OpenGL 3.3**
   with an SDL3 window, on Linux and Windows (ADR-0008).
3. **Enhance later.** Once parity is verified, optionally raise rendering quality
   (higher resolution, filtering, true 3D lighting) behind toggles.
4. **Preservation & appreciation.** Credit the original authors prominently; this
   is a fan-preservation effort, not a commercial product.

## Current status

Early implementation (the asset layer is solid; the simulation is just beginning).
What works today:

- **Asset pipeline** — clean-room readers for `WRS` (LZSS archives), `PCX` (8-bit
  paletted images), `WDL` (lexer + parser + `INCLUDE`/`DEFINE`/`IFDEF`/`IFNDEF` preprocessor), `WMP`
  (text maps), `WAV`, and 8-bit Autodesk `FLI`/`FLC`, validated against retail data via guarded,
  local-only integration tests. Inspectable with the `ovtool` CLI.
- **Headless level load** — `LevelLoader` combines a level's flattened WDL with its
  WMP map into a typed object model (regions, walls, things, actors, skills, actions,
  textures). Guarded local-only tests cover all six retail archives through level
  loading, inspection mesh generation, texture decode, and runtime boot/tick.
- **OpenGL 3.3 renderer** — an SDL3 windowed viewer draws textured walls and ear-clipped
  floors/ceilings with a depth buffer, plus camera-facing **billboard sprites** for
  things and actors (palette-index-0 color-key transparency). It recognizes WDL
  `FLAGS SKY` textures as the measured panorama (OV-20261029-sky), renders open
  portal wall spans, alpha-tests palette-index-0 holes only on walls marked
  `TRANSPARENT`, and combines global/texture/region ambient lighting with
  `PLAYER_LIGHT`. Static sprite billboards are
  laid out in the engine layer and drawn back-to-front for deterministic static scenes.
  The camera is driven by the WDL-visible player with radius-aware wall, portal,
  and entity collision. Floor/ceiling queries interpolate WMP vertex heights in
  regions with slope flags (`FLOOR_ASCEND`/`LIFTED` raise the floor, `FLOOR_DESCEND` lowers it,
  `CEIL_ASCEND` raises the ceiling, `CEIL_DESCEND`/`LIFTED` lower it; OV-20261029-slope), so
  authored slopes feed vertical collision, WDL height/slope skills, and the
  rendered feet/shadows of things and actors. The tilt-0 synthetic room matches the
  DOSBox-X capture `OVTLT0.PCX` (stripes G R G R G R, floor at the bottom). The previous
  Direct3D 11 viewer remains in `src/legacy/` and builds on Windows only.
- **WDL interpreter — foundation** — a full expression evaluator (arithmetic,
  comparison, logical `&&`/`||`, bitwise integer operators, modulo, documented
  single-argument math functions, member access, and A3-style arithmetic aborts)
  and `SET`/`RULE`/`IF`/`WHILE`/`BREAK`/`CONTINUE`/`CALL`/`GOTO`/`END` statements run against a live skill table
  (`WdlRuntime`); a level's `IF_START` script boots in the viewer, and a per-frame
  `Tick` maintains the fixed-tick `TIME_CORR` factor. Skill assignments respect
  declared `MIN`/`MAX` bounds. Runtime registration now tracks placed things/actors
  for iteration, exposes unique placement names for script member access, and registers
  the debug player as a script-visible object in the viewer. A provisional scheduler
  API dispatches registered per-frame object actions with `my` and `TIME_CORR` bound.
  The app exposes live keyboard/mouse polling and movement-force skills, consumes
  script-authored player velocity/rotation, and now runs Apathy's retail
  label/`GOTO` movement action after startup; a guarded integration test verifies
  sustained input produces valid collision-aware movement. Player hitscan and
  actor sight checks traverse visible portal openings and dispatch retail
  `IF_HIT` actions. Player shots use the live directional animation frame and
  alpha mask, continue through transparent sprite pixels, and publish sprite
  `HIT_X`/`HIT_Y`; wall hits report offset-aware texture coordinates for attached
  decals. Normalized `SHOOT_X`/`SHOOT_Y` deviations go through the engine's measured
  `ProjectionModel` (`PLAYER_ARC`, `ASPECT`, `PLAYER_TILT`, the 3-D window size), the same model
  the renderer uses. Apathy's real left-input weapon path is covered by a guarded
  runtime test and rendered smoke. The app also
  renders first-pass WDL text, panels, buttons, sliders, bars, and bitmap widgets;
  visible text with `IF_KLICK` uses its live rendered glyph bounds for hit testing.
  `MSPRITE` metadata is preserved separately from its overlay and follows the
  documented `MOUSE_MODE` visibility and mouse-position/hotspot behavior. The
  app publishes per-frame virtual-pixel `MICKEY_X/Y` deltas, leaves active
  `MOUSE_X/Y` coordinates under script control, tracks `MOUSE_MOVING`, dispatches
  `IF_MSTOP`, and hides the native cursor over the custom pointer surface. In
  mouse modes 0/1, bounded `MOUSE_SENSE`-scaled deltas feed the retail movement
  action through `FORCE_ROT`/`FORCE_TILT`; mode 2 remains cursor-only.
  An eight-voice software mixer feeding an SDL3 audio stream plays overlapping retail WAV effects with immediate
  `CHANNEL`/`CHANNEL_0..7` state, global and per-sound volume, numeric stereo pan,
  and first-pass object-relative `SDIST`/`SVDIST` attenuation. Texture `SOUND`,
  `SVOL`, `SCYCLES`, and `SLOOP` metadata is retained; grounded movement triggers
  floor sounds and entering a region starts or loops its ceiling sound through the
  same eight-channel host. Looping wall, thing, and actor textures occupy channels
  within earshot and continuously update attenuation and stereo direction. Animated
  wall and entity textures fire non-looping sounds on their authored `SCYCLES`
  phases after host-side distance filtering.
  Skill loading preserves whether `VAL` was explicitly authored, so bare
  declarations of predefined engine skills receive their documented defaults
  without overriding intentional zero initializers.
  The retail title sequence now decodes and plays its full-screen FLC at the
  authored cadence while world simulation pauses. As measured against the DOS
  runtime (OV-20261015-flic-play-blocking), the action that starts the flic goes on
  at once, the global scheduler and waiting actions keep running, a key during the
  logo stops it, and `FLIC_FRAME` counts the frame on screen. The title proceeds
  through the keyboard-driven title/menu flow into Apathy in one host process.
- **Projection parity** — the 3-D projection reproduces the original as measured black-box
  against the DOS runtime (OV-20261009 notes): the horizontal field of view is `PLAYER_ARC`
  radians, the vertical scale is three quarters of the window height over the same tangent
  (divided by `ASPECT`), `PLAYER_TILT` is a y-shear of a full window height per unit, and
  every VGA mode is presented at the 4:3 display aspect. The hitscan `SHOOT_X`/`SHOOT_Y` rays
  are measured too (OV-20261009-shoot-mapping, eight runs): they go through the same tangents,
  positive `SHOOT_X` aims right and positive `SHOOT_Y` **up**, the tilt shear moves the ray,
  `HIT_DIST` is the hit's depth along the line of sight, `HIT_X`/`HIT_Y` count from the bitmap's
  upper-left as drawn, and `RESULT = SHOOT_FAC · (1 − HIT_DIST / SHOOT_RANGE)`. The world is
  drawn with the original's handedness — facing map +x, the map's +y side is on the screen left,
  as the same screenshots show — through a single map→render mapping (render z = −map y, camera
  yaw = `PLAYER_ANGLE` + π/2; ADR-0009), a WMP `PLAYER_START` angle of 0 faces map +x like the
  oracle dump, thing bitmaps are unmirrored and a wall's bitmap starts at its second vertex for a
  viewer in its left region. Engine tests pin all of this. The tilt-0 frame was checked on the
  GL host on 2026-10-15. The retail title→Apathy smoke passed on the GL host the same day
  (`RetailSmokeTests`): logo FLC, splash, menu and keyboard handoff into Apathy at its declared
  start region, and a drawn frame from all six retail archives. New Game goes through the
  difficulty menu, now that `BRANCH` is the tail jump measured against the DOS runtime
  (OV-20261015-branch-tail-jump). The 16 global `EACH_SEC` slots, which the manuals never
  document, are measured too (OV-20261015-each-sec-cadence): they all run once per engine second
  (16 ticks) when the whole-second `SECS` count goes up, just before the global `EACH_TICK` slots.
  The title's logo and splash time out on their own, and `HEART`, `LEGALISM` and
  `NEWAGE` finish their three-second fade-in and draw their HUD, crosshair and weapon.
- **Collision and proximity parity** — measured against the DOS runtime in nine oracle notes
  (OV-20261019-*). `IF_NEAR`/`IF_FAR` use |dx| + |dy| against a strict `DIST`. For things and
  actors the eye must also be within `DIST` of the texture's centre. Crossing or pushing a wall
  fires `IF_NEAR`. `CLIP_DIST` gates object actions, movement, animation and `DIST` tests.
  `LIBER`, `BERKELEY` things, `IMPASSABLE` and `INVISIBLE` walls are handled. The player climbs
  region borders from the feet with a clamped eye. Actors without `CAREFULLY` push the player,
  and `CAREFULLY` ones stop short of it, honour `IMPASSABLE` and climb below `ACTOR_CLIMB`.
  The player's contact distance with an object is √(c² + 0.093), where c is half the object's
  width plus `PLAYER_WIDTH`. A player starting or beamed inside objects is pushed out of the last
  placed overlapping one only (OV-20261029-overlap).
  A headless view test (ADR-0010, OV-20261020-view-activity) sets `VISIBLE` and `SEEN` on things,
  actors, walls and regions, lets unseen actors skip frames (`SKIP_FRAMES`) and puts unseen
  `BERKELEY` walls to sleep. Things' and actors' `DISTANCE`, which the `DIST` test reads, is
  refreshed only when the view changes or an actor in front of the player walks
  (OV-20261021-distance-refresh).

- **Stacked (`BELOW`) regions** — the retail maps list only the top region of a stack; the 617
  lower storeys across the four levels exist only as WDL declarations. The loader now builds each
  one under its map region with the declared heights and the top region's footprint (manual p.59–61),
  so they get floors, ceilings and walls (a wall covers what is open on one side and solid on the
  other, such as a bridge's slab), the player walks into the storey it fits, and underwater
  `IF_DIVE`/`IF_ARISE` links resolve. Things and actors start on the uppermost storey, or with
  `GROUND` on the lowest storey whose ceiling is above their feet (manual p.61). The view test
  sees through the openings between storeys and not through the slabs (OV-20261029-below-view).
  A lower storey's wall pieces show the texture's next four sides (manual p.38). Still open:
  two- and four-sided wall textures (which half or face shows which side).
- **Engine rules measured on 2026-10-29** — eight oracle notes (OV-20261029-*):
  - The cycle order: resumed waits, then `EACH_SEC`, the global slots, and the regions, actors,
    things and walls, each kind in reverse map order. `IF_START` and the first cycle have
    `TICKS` 0.
  - Numbers are 22.10 fixed point: literals truncate and saturate, arithmetic wraps, and
    functions truncate.
  - The `ACCEL` formula, and a default `FRICTION` of 0.
  - Actors step by |dx| + |dy|, arrive within 0.125, and fire `IF_ARRIVED`/`IF_HIT` in the
    measured order.
  - Save files: OpenVirtue writes `SAVE_INFO` files byte for byte. It reads the skills and the
    things', actors' and walls' state of full saves.
  - The sky panorama.
  - Palette-index shading by distance and `PLAYER_LIGHT`, and the shade table for sorted
    ranges and `AUTORANGE`.
  - Which slope flags tilt a region.
  - Texture animation phase lengths: each loop is fixed in frames from the `TIME_CORR` it
    starts with.
  - How sounds take the eight channels: lowest free, then the oldest; `SLOOP` channels are
    kept; skipped sounds; when channels read free, and `STOP_SOUND`.
- **Oracle-diff harness** — `ovtool oracle check <probe>` prepares a probe's DOSBox-X carriers,
  runs them headless (`oracle run`) and diffs them (`oracle diff`). The diff replays each carrier
  through the headless `RuntimeHost` in step with the original run's per-cycle `TICKS` and compares
  every trace file the DOS runtime wrote with OpenVirtue's, line by line, and every save section by
  section, naming the sections and skills that differ.
  `oracle summary` gives every probe's match count. These probes match in full:
  - projection, pixel aspect, tilt;
  - `BRANCH`;
  - the sky, the shading curve, the shade table;
  - slopes.

  The timed ones list what is still off: start-up `WAITT` timing, sub-percent movement steps,
  details the notes left open, and full `SAVE` files, whose objects' view words and bits
  OpenVirtue does not reproduce.

Not yet complete:
- exact UI behaviour beyond `INKEY` editing (OV-20261029-inkey) and `DIGITS`/`TEXT` layout
  (OV-20261029-ui-layout), panel widgets (OV-20261029-ui-widgets) and multi-line text
  (OV-20261029-ui-text) and slider knobs: dragging a slider, and where a click lands relative to the
  pointer's hot spot;
- how floor and texture-cycle sounds take channels;
- things `LOCATE`d into a `BELOW` stack (a player lands in the storey that holds its eye,
  OV-20261030-below-beam; sided wall textures are measured and drawn, OV-20261030-wall-sides);
- full save compatibility in play. OpenVirtue:
  - reads and writes `SAVE_INFO`;
  - imports the original's full saves (skills, objects, waiting actions, slots);
  - writes full saves the original loads (`RuntimeHost.WriteOriginalSaves`, off by default). The
    original loads OpenVirtue's saves of the retail `apathy` and `legalism` levels. That covers a save
    made at the level's start and ones made in play with an action waiting, which the original resumes
    at its own pace. The `SAVE` textures and `SAVE` regions sections match the original's byte for byte
    on `legalism` (OV-20261029-save-body rounds 13 to 15);
  - writes the screen elements' section as measured, though LEGALISM's own save orders some
    elements differently, for a reason not yet found (rounds 18 and 19); does not yet write two run-time words of the first section,
    the engine's run-time statistics skills (`TIME_*`, `ACTIONS`, `ACTIVE_NEXUS`, `MAX_DIST`,
    `VESA_MODE`) or `INKEY` strings. V3.963 itself hung loading its own save while an object's
    event action waited (round 17), so such saves are written as the original writes them but not
    proven to load;
- `CAREFULLY` actors' residuals and slides (steps along an `ANGLE` and plain steps toward a
  point match unit for unit: the engine rounds its own products, OV-20261029-actor-step round 6);
- shots at mirrored sprites and walls: their `HIT_X` (a sprite's fractional `HIT_X` matches,
  OV-20261009-shoot-mapping);
- start-up `TICKS` timing, which keeps many timed probes from matching line for line
  (`ovtool oracle summary` lists each probe's count).
**The app is a level viewer with debug walking, not yet a playable game.** See the
[milestone roadmap](docs/recon/06-reimplementation-strategy.md) for the full plan
and where each piece sits. The immediate work plan is tracked in
[`docs/planning/near-term-roadmap.md`](docs/planning/near-term-roadmap.md).

## What you need to play (planned end-user flow)

1. Legally obtain Saints of Virtue (retail CD or archive).
2. Point OpenVirtue at the install folder containing the `*.WRS` data files.
3. Run OpenVirtue. (No game files ship with the engine.)

## Repository layout

| Path | Purpose |
|------|---------|
| `docs/recon/` | Research findings (engine, formats, prior art, legal, tooling). |
| `docs/adr/` | Architecture Decision Records (created as we make design choices). |
| `docs/clean-room/` | Oracle/invariant discovery protocol and sanitized observation notes. |
| `docs/planning/` | Current near-term work plan. |
| `src/` | Engine source: `OpenVirtue.Formats` (asset readers), `OpenVirtue.Engine` (world model, WDL interpreter, geometry), `OpenVirtue.Host` (frame loop), `OpenVirtue.App` (SDL3 + OpenGL viewer). |
| `tools/` | Local asset-inspection / extraction helpers (not redistributed game data). |
| `tests/` | Automated tests. |
| `_research/` | **Git-ignored.** Local-only copies of the game, the SaintsX fan patch, and third-party reference code — for study only, never committed or shipped. |

## Credits & acknowledgements

This project exists out of appreciation for the people who made the original:

- **Shine Studios** — original game.
- **Cactus Game Design** — publisher.
- **Conitec Datensysteme GmbH** — the Acknex / 3D GameStudio engine.

Community research that informs (but is **not** copied into) this project:
firoball's `WDL2CS` / `WMPio` / `AcknexCSApi`, rickomax's WRS work, and the
**SaintsX** fan patch. See [`docs/recon/03-prior-art.md`](docs/recon/03-prior-art.md)
and the licensing notes before reusing any of it.

## License

**GNU GPL v3.0-or-later** — see [`LICENSE`](LICENSE) and
[ADR-0003](docs/adr/0003-license-gplv3.md). The OpenMW model: the engine and its
derivatives stay open, and the GPL is compatible with our deps (.NET runtime and
Vortice.Windows are both MIT).

> OpenVirtue is a clean-room reimplementation. We use the original game, the
> SaintsX patch, and third-party Acknex-3 projects as **reference only** — never
> copying their code — to keep this GPLv3 codebase clean of incompatibly-licensed
> lineage. See [ADR-0005](docs/adr/0005-clean-room-reference-only-policy.md).
> Hidden runtime behavior is discovered through sanitized oracle observation
> records; see [ADR-0007](docs/adr/0007-oracle-based-invariant-discovery.md) and
> [`docs/clean-room/`](docs/clean-room/).

New source files carry:

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors
```

"Saints of Virtue", "Acknex", and "3D GameStudio" are trademarks of their
respective owners and are used here nominatively only.
