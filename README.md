# OpenVirtue

[![CI](https://github.com/TheSameCat2/OpenVirtue/actions/workflows/ci.yml/badge.svg)](https://github.com/TheSameCat2/OpenVirtue/actions/workflows/ci.yml)

A clean-room, open-source reimplementation of the **Acknex-3 (3D GameStudio A3)**
engine runtime, with the goal of running the 1999 Christian FPS **Saints of
Virtue** natively on modern Windows — and, once parity is reached, improving its
graphics using native technologies (Direct3D).

This project follows the **OpenMW / engine-remake model**: we ship *only* our own
code. The end user supplies their own legally obtained copy of the original game,
whose data files our engine reads at runtime. **No original game assets, scripts,
or executables are distributed with this project.**

> Status: **early implementation.** The asset pipeline (WRS/PCX/WDL/WMP/WAV) works
> and a textured level renders in a Direct3D 11 window with billboard sprites; the
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
2. **Native & modern.** Written in **C#/.NET 10**, rendering through **Direct3D**
   (via Vortice.Windows), following modern Windows game-dev practice.
3. **Enhance later.** Once parity is verified, optionally raise rendering quality
   (higher resolution, filtering, true 3D lighting) behind toggles.
4. **Preservation & appreciation.** Credit the original authors prominently; this
   is a fan-preservation effort, not a commercial product.

## Current status

Early implementation (the asset layer is solid; the simulation is just beginning).
What works today:

- **Asset pipeline** — clean-room readers for `WRS` (LZSS archives), `PCX` (8-bit
  paletted images), `WDL` (lexer + parser + `INCLUDE`/`IFDEF` preprocessor), `WMP`
  (text maps) and `WAV`, each validated against all six retail archives via guarded,
  local-only integration tests. Inspectable with the `ovtool` CLI.
- **Headless level load** — `LevelLoader` combines a level's flattened WDL with its
  WMP map into a typed object model (regions, walls, things, actors, skills, actions,
  textures).
- **Direct3D 11 renderer** — a windowed viewer draws textured walls and ear-clipped
  floors/ceilings with a depth buffer, plus camera-facing **billboard sprites** for
  things and actors (palette-index-0 color-key transparency). The camera is now
  driven by a first-person debug player with floor following, point-based portal
  crossing/blocking, gravity, and jump.
- **WDL interpreter — foundation** — a full expression evaluator (arithmetic,
  comparison, logical `&&`/`||`, modulo, member access) and `SET`/`RULE`/`IF`
  statements with action-to-action calls run against a live skill table
  (`WdlRuntime`); a level's `IF_START` script boots in the viewer, and a per-frame
  `Tick` maintains the fixed-tick `TIME_CORR` factor. Skill assignments respect
  declared `MIN`/`MAX` bounds.

Not yet complete: the `each_cycle` scheduler dispatch, Acknex-accurate
player-movement parity, body-radius/sliding collision, actor animation/AI, audio
playback, HUD/menus/inventory, save/load, and the DOSBox-X oracle-diff harness.
**The app is a level viewer with debug walking, not yet a playable game.** See the
[milestone roadmap](docs/recon/06-reimplementation-strategy.md) for the full plan
and where each piece sits.

## What you need to play (planned end-user flow)

1. Legally obtain Saints of Virtue (retail CD or archive).
2. Point OpenVirtue at the install folder containing the `*.WRS` data files.
3. Run OpenVirtue. (No game files ship with the engine.)

## Repository layout

| Path | Purpose |
|------|---------|
| `docs/recon/` | Research findings (engine, formats, prior art, legal, tooling). |
| `docs/adr/` | Architecture Decision Records (created as we make design choices). |
| `src/` | Engine source: `OpenVirtue.Formats` (asset readers), `OpenVirtue.Engine` (world model, WDL interpreter, geometry), `OpenVirtue.App` (Direct3D 11 viewer). |
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

New source files carry:

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors
```

"Saints of Virtue", "Acknex", and "3D GameStudio" are trademarks of their
respective owners and are used here nominatively only.
