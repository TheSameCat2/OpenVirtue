# Reimplementation Strategy

This is a research-phase recommendation, not a committed plan. Before we write
engine code we should run a proper **design/brainstorming pass** and capture
choices as ADRs in `docs/adr/`.

> **Status update (2026-06-26):** implementation has begun and the architecture
> below is the early sketch — the realized layout differs. The WDL interpreter lives
> in `OpenVirtue.Engine` (not a separate `OpenVirtue.Wdl`); there is no separate
> `Rendering`/`Platform` project (GPU-agnostic geometry is in `Engine/Rendering` and
> Direct3D lives in `OpenVirtue.App`). The milestone table below is annotated with
> current progress. For authoritative current status see the
> [root README](../../README.md#current-status); for current structure, the code and
> [`AGENTS.md`](../../AGENTS.md).

## Guiding principles

1. **Parity before polish.** Reproduce Acknex-3 behavior exactly — including its
   quirks and fixed-tick timing — *then* add enhancements behind toggles.
2. **Black-box the original.** Learn from published format docs, our own byte
   analysis, and the **DOSBox-X oracle** — never from copied/decompiled code.
3. **Data-driven, asset-free distribution.** Engine reads the user's files; ships
   none.
4. **Validate continuously** against the oracle and against tiny golden tests.

## Interpreter, not transpiler — and why

firoball's stack **transpiles** WDL→C# at build time and runs it in Unity. For a
faithful *native* engine we recommend a **WDL interpreter** instead:

- The game ships **many WDL modules per world**; an interpreter loads the user's
  actual scripts at runtime — no per-game C# generation, no derived-code
  artifacts to license, cleaner OpenMW-style separation.
- Behavior (especially the `each_cycle`/scheduler tick and skill arithmetic) is
  easier to match precisely when we *are* the runtime than when we lean on C#'s
  semantics post-transpile.
- We can still keep a transpile option later if profiling demands it.

## Proposed architecture (sketch — to be ADR'd)

```
OpenVirtue.sln
├─ src/
│  ├─ OpenVirtue.Formats      // WRS, PCX, WAV, WDL(lex/parse/AST), WMP, MDL, FLC
│  │                          //   net10.0, no graphics deps, heavily unit-tested
│  ├─ OpenVirtue.Wdl          // WDL interpreter: objects, skills, actions,
│  │                          //   scheduler/tick, command set (the "API")
│  ├─ OpenVirtue.Engine       // world model (regions/walls/things/actors/ways),
│  │                          //   collision, movement, game loop, save/load
│  ├─ OpenVirtue.Rendering    // Direct3D 11 (Vortice): 2.5D sector renderer,
│  │                          //   palette/texture mgmt, sprites, HUD/overlays
│  ├─ OpenVirtue.Platform     // Win32 host, window/message loop, XAudio2 audio,
│  │                          //   XInput/DirectInput, timing
│  └─ OpenVirtue.App          // launcher: locate game dir, config, boot world
├─ tools/
│  ├─ wrsdump / pcxdump / wmpdump / wdldump  // CLI inspectors → text/JSON
│  └─ oracle-diff             // harness comparing our output to DOSBox-X
└─ tests/                     // xUnit golden-file & behavioral tests
```

Layering rule: `Formats` depends on nothing; `Wdl`/`Engine` depend on `Formats`;
`Rendering`/`Platform` depend on `Engine`; only `App` wires it all. Keep DirectX
out of `Formats`/`Wdl`/`Engine` so they stay testable headless.

## Milestone roadmap (each milestone is demoable)

Status legend: ✅ done · 🚧 in progress · ⬜ not started.

| # | Milestone | Proves | Status |
|---|-----------|--------|--------|
| **M0** | Repo, CI, ADRs, format inspector skeleton | Process works. | ✅ |
| **M1** | **WRS reader (LZSS)** + `wrsdump` | We can extract the user's data ourselves. | ✅ (`ovtool wrs`) |
| **M2** | **PCX + palette** viewer | We can see original textures/sprites. | ✅ (`ovtool pcx`) |
| **M3** | **WDL lexer/parser → AST** + `wdldump` | We understand the scripts structurally. | ✅ (+ preprocessor, `ovtool wdl`) |
| **M4** | **WMP loader** → in-memory world; render a level's geometry **flat/wireframe** in a Direct3D window | Geometry pipeline + renderer bring-up. | ✅ |
| **M5** | **Textured 2.5D renderer** (regions, walls, sky, palette shading) | Visual parity of a static level. | 🚧 walls + floors/ceilings textured; **no sky or palette shading** |
| **M6** | **WDL interpreter core**: skills, actions, scheduler tick, player movement matching original math (use `move-wdl` as reference) | The world comes alive; movement parity. | 🚧 full expression evaluator (incl. `&&` `\|\|` `%`), `SET`/`RULE`/`IF`, action calls, `IF_START` boot in the app, per-frame `TIME_CORR`, skill `MIN`/`MAX` clamping, and first-cut point-based player walking/jump; **no each_cycle dispatch or movement parity yet** |
| **M7** | **THINGs/ACTORs**: sprites, animation, basic AI hooks (`if_near`/`if_hit`) | Enemies/objects behave. | 🚧 static billboard sprites render; **no animation or AI** |
| **M8** | **HUD/menus/inventory/scrolls/2-D map** (UI widget system) | Full UI parity. | ⬜ |
| **M9** | **Audio** (WAV/XAudio2), **save/load**, weapons, traps, doors | Playable level start→finish. | ⬜ (WAV *reader* exists; no playback) |
| **M10** | **All five worlds** boot and complete; oracle-diff green | **Parity achieved.** | ⬜ |
| **M11+** | Enhancements behind toggles: hi-res, filtering, true-3D lighting, widescreen | Your "improve graphics" goal. | ⬜ |

## Biggest technical risks (watch early)

- **Fixed-tick timing semantics.** Get `TIME_CORR`/tick model right in M6 or
  every downstream behavior drifts. The SaintsX `move-wdl`/`weapon-wdl` patches
  document the original constants.
- **WMP↔WDL coupling.** A map is meaningless without its WDL object definitions
  (WMPio's own caveat). M3 and M4 must be developed together.
- **Palette & paletted lighting.** 8-bit color with palette shading must be
  reproduced before "it looks right" is achievable (M5).
- **LZSS exactness.** Subtle window/flag mismatches corrupt everything; golden-test
  M1 against QuickBMS output before building on it.
- **Collision/"walk through walls."** The original is approximate and exposes a
  manual fix key; match it in M6 rather than "fixing" it.

## Immediate next actions (when you're ready to build)

1. Decide **license** + **project name** (see legal doc).
2. Run a **brainstorming/design pass** on the architecture above; write ADR-0001
   (renderer = Direct3D 11 via Vortice) and ADR-0002 (interpreter vs transpiler).
3. Email firoball / TheSameCat2 about permissive relicensing (could change the
   build-vs-reference calculus dramatically).
4. Scaffold `OpenVirtue.sln` + `OpenVirtue.Formats` + xUnit, then **M1 (WRS)**.
