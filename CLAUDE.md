# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

OpenVirtue is a **clean-room reimplementation of the Acknex-3 (3D GameStudio A3) engine
runtime**, aiming to run the 1999 FPS *Saints of Virtue* natively on modern Windows in
C#/.NET 10 with a Direct3D 11 renderer. It follows the **OpenMW model**: the engine ships
*only its own code* and reads the user's own legally-obtained game data (`*.WRS` files) at
runtime. No original game assets, scripts, or executables are distributed.

Parity-first is the design philosophy: faithfully reproduce Acknex-3 behavior (including
its fixed-tick timing and quirks) *before* adding enhancements behind toggles.

## Clean-room policy — read before writing any reader/parser

This is the project's defining constraint (ADR-0005). The best Acknex-3 references
(firoball's `WDL2CS`, `WMPio`, `AcknexCSApi`) are **CC BY-NC / GPL-incompatible**, and the
original game/patch are proprietary. They all live under the git-ignored `_research/` folder.

- **Never copy, port, or paste code** from `_research/` or any third-party/decompiled source.
- Extract *facts only* (format layouts, algorithm/behavior descriptions), restate them in
  your own words in `docs/`, and implement from `docs/` + your own byte/behavior analysis.
- Each non-trivial format reader carries a **provenance note** explaining how the layout was
  learned (published spec, own analysis) and explicitly *not* "ported from <repo>". See
  [src/OpenVirtue.Formats/PROVENANCE.md](src/OpenVirtue.Formats/PROVENANCE.md) — keep it
  updated when you add or change a reader.

## Commands

Toolchain: .NET SDK 10.0.301. The solution uses the **`.slnx`** (XML) format, not `.sln`.

```bash
dotnet build OpenVirtue.slnx                      # build all (analyzers run here; warnings, not errors)
dotnet test  OpenVirtue.slnx                      # run all tests (xUnit)
dotnet test  tests/OpenVirtue.Formats.Tests       # run one test project
dotnet test --filter "FullyQualifiedName~WrsArchiveTests"   # run one class/test by name

# Run the game (Windows; point at one of YOUR OWN .WRS files):
dotnet run --project src/OpenVirtue.App -- path/to/apathy.wrs [MAIN.WDL]
#   Controls: WASD move, Q/E down/up, hold Shift faster, drag left-mouse to look.

# Inspect game files with the `ovtool` CLI:
dotnet run --project tools/OpenVirtue.Tools -- wrs list    <archive.wrs>
dotnet run --project tools/OpenVirtue.Tools -- wrs extract <archive.wrs> [out-dir]
dotnet run --project tools/OpenVirtue.Tools -- level info  <archive.wrs>   # also: pcx|wmp|wav|wdl info
```

There is no separate lint step — .NET analyzers (`AnalysisLevel=latest-recommended`) run as
part of the build. There is no CI configured.

## Architecture

The pipeline turns a user's archive into a rendered level:

```
WRS archive ──► WDL (preprocess/flatten INCLUDE+IFDEF) ─┐
            └─► WMP (text map: geometry + placements) ──┴─► Level ──► TextureLoader ──► LevelWindow
                                                          (typed objects   (PCX→RGBA)   (Direct3D 11)
                                                           + texture catalog)
```

Projects and their **strict layering** (enforced only by discipline — keep it):

- **`OpenVirtue.Formats`** (`net10.0`, **zero dependencies**) — pure, headless, heavily
  unit-tested readers for the binary/text formats: `Wrs` (LZSS-compressed archive), `Pcx`
  (8-bit paletted images), `Wav`, and `Wdl` (lexer → parser → preprocessor producing a
  *generic* syntax tree that captures structure only) and `Wmp` (text map emitted by the
  WED editor). No graphics, no engine concepts.

- **`OpenVirtue.Engine`** (`net10.0`, → Formats, **no graphics dependency**) — the world
  model, interpreter, and *API-agnostic* geometry:
  - **Object model** (`AcknexObject`, `Map/` = `Region`/`Wall`/`Thing`/`Actor`/`Vertex`/`PlayerStart`):
    a **hybrid** model (ADR-0006) — well-known fields are typed properties, everything else
    (custom skills, synonyms, reflective `my.skill` access) lives in a per-object dynamic
    table; `obj["name"]` bridges the two so the interpreter reads/writes uniformly by name.
  - **`Interpreter/`** — a **WDL interpreter, not a transpiler** (ADR-0002). `WdlRuntime`
    is the live environment (skill table, object registry, actions) implementing `IWdlContext`;
    `WdlInterpreter` executes statement blocks; `WdlExpression`(+`Parser`) evaluates expressions.
  - **`LevelLoader` / `Level` / `WdlDeclarations`** — the glue that combines a level's WDL
    program with the WMP map it names, indexes declarations (`BMAP`→`TEXTURE`, `REGION`/`WALL`/
    `THING`/`ACTOR` type defs, `SKILL`s, `ACTION`s), and materializes typed engine objects.
  - **`Rendering/`** — `MeshBuilder`, `EarClipping`, `LevelMesh`, `RenderVertex`, `TextureLoader`:
    turns geometry into vertex buffers and decodes PCX→RGBA `TextureImage`s. **No DirectX here**,
    deliberately, so the engine stays testable headless.

- **`OpenVirtue.App`** (`net10.0-windows`, WinForms `WinExe`, → Engine) — the **only place
  DirectX lives** (Vortice.Direct3D11/DXGI/D3DCompiler/Mathematics). `Program` is the entry
  point; `LevelWindow` is the D3D11 host (swap chain, depth buffer, inline HLSL shader,
  sprite billboards, alpha-test color-key cutout) with a fly `Camera`.

- **`OpenVirtue.Tools`** (`net10.0`, → Formats + Engine) — the `ovtool` CLI inspector.

**Key coupling to remember:** a WMP map is meaningless on its own — it carries geometry and
placements but references texture/type/skill definitions that only exist in the level's WDL.
`LevelLoader` is where the two halves meet; changes to either format reader usually ripple here.

## Conventions

- **Every source file starts with the SPDX header** (the build does not enforce this, so add
  it by hand):
  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 The OpenVirtue Authors
  ```
- Common build settings are centralized in `Directory.Build.props` (nullable + implicit
  usings enabled, analyzers on, XML docs generated with CS1591 suppressed). Don't re-declare
  these per project.
- C# style (`.editorconfig`): file-scoped namespaces that **match the folder**, Allman braces
  (newline before `{`), braces always, `System.*` usings sorted first, 4-space indent.
- Tests: xUnit, named `Method_Scenario_Expectation` (CA1707 is disabled for `tests/`).

## Testing against real game data

Unit tests use tiny hand-built byte vectors and run anywhere. In addition, **guarded
integration tests** validate readers against real, user-supplied `.WRS` files if they are
present under the git-ignored `_research/` folder, and are a **no-op otherwise**
(`TestSupport/ResearchData` locates `_research/` by walking up to `OpenVirtue.slnx`). Never
commit game data or fixtures.

## Source-of-truth notes

- **`docs/adr/`** records accepted design decisions and is authoritative; write a new ADR
  when you make a significant architectural choice.
- **`docs/recon/`** is research-phase background (dated 2026-06-25). Its proposed
  architecture predates the code and **does not match the current layout** (there is no
  separate `OpenVirtue.Wdl`/`Rendering`/`Platform` project; the WDL interpreter lives in
  `Engine`, rendering is split between `Engine.Rendering` and `App`). The strategy doc's
  milestone table is annotated with current progress, but the **code is the source of truth**
  for structure.
- The root [`README.md`](README.md) is current: its **Current status** section is the
  authoritative status summary, and `src/README.md` / `tests/README.md` describe the actual
  projects (refreshed 2026-06-26 to match the code).

## Domain glossary

- **WRS** — game archive (LZSS-compressed, no file header). **WDL** — the engine's scripting
  language. **WMP** — text map file (vertices, regions, walls, things/actors, player start).
- **REGION** — a floor/ceiling sector; **WALL** — an edge between regions; **THING** — a
  static sprite object; **ACTOR** — an animated/AI sprite object.
- **SKILL** — a WDL variable/property (objects have ~hundreds). **ACTION** — a named WDL
  script (function). **BMAP/TEXTURE** — bitmap and the texture definition that references it.
- **`my` / `you`** — the active object references in WDL actions. **`each_cycle`** — the
  per-tick scheduler hook. **TIME_CORR** — the fixed-tick timing constant (parity-critical).
