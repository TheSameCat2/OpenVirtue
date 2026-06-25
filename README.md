# OpenVirtue

A clean-room, open-source reimplementation of the **Acknex-3 (3D GameStudio A3)**
engine runtime, with the goal of running the 1999 Christian FPS **Saints of
Virtue** natively on modern Windows — and, once parity is reached, improving its
graphics using native technologies (Direct3D).

This project follows the **OpenMW / engine-remake model**: we ship *only* our own
code. The end user supplies their own legally obtained copy of the original game,
whose data files our engine reads at runtime. **No original game assets, scripts,
or executables are distributed with this project.**

> Status: **Reconnaissance & research.** No engine code has been written yet.
> See [`docs/recon/`](docs/recon/) for the full research findings.

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

## What you need to play (planned end-user flow)

1. Legally obtain Saints of Virtue (retail CD or archive).
2. Point OpenVirtue at the install folder containing the `*.WRS` data files.
3. Run OpenVirtue. (No game files ship with the engine.)

## Repository layout

| Path | Purpose |
|------|---------|
| `docs/recon/` | Research findings (engine, formats, prior art, legal, tooling). |
| `docs/adr/` | Architecture Decision Records (created as we make design choices). |
| `src/` | Engine source (empty — implementation has not started). |
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
