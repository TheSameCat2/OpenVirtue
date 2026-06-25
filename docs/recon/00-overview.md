# Reconnaissance — Executive Summary

*Date: 2026-06-25. Phase: research only; no engine code written.*

## What we're reimplementing

**Saints of Virtue** (Shine Studios, 1999) is built on **Acknex-3** — the third
generation of Conitec's *Acknex* engine, later marketed as **3D GameStudio**.
Acknex-3 (the "A3" engine) is a **Doom-era 2.5D portal/sector engine** with
sprite-based actors, *not* the true-3D A4+ generation. Its content is described
by the **WDL** scripting language plus binary map (`WMP`) and resource (`WRS`)
files.

This matters: the rendering model is a **sector/portal world (REGIONs bounded by
WALLs) with billboard THINGs/ACTORs**, closer to Doom/Build than to Quake. The
reimplementation target is therefore a software-style 2.5D world model rendered
through a modern GPU pipeline — not a general 3D scene graph.

## The two existing "ways to run it"

1. **SaintsX** (fan patch, bundled in `_research/SaintsX113`): does **not**
   reimplement anything. It binary-patches the retail data, then runs the
   original **DOS** `VRUN.EXE` inside **DOSBox-X**. Useful as a *reference
   implementation oracle* (we can diff our behavior against it) and its `.patch`
   files are readable WDL diffs that reveal game logic.
2. **firoball's Unity stack** (`WDL2CS` + `AcknexCSApi` + `WMPio` + `WRSExtractor`):
   transpiles WDL → C# and runs it against a hand-written Acknex-3 API, targeting
   **Unity**. This is the closest prior art to what we want, but it is
   Unity-bound and **licensed CC BY-NC** (see below).

Our project is distinct from both: a **native C#/Direct3D** reimplementation of
the Acknex-3 *runtime*, distributed without game files (OpenMW model).

## The single most important finding (legal)

firoball's `WDL2CS` and `WMPio` are **CC BY-NC 4.0** (Attribution-**NonCommercial**)
— *not* an OSI open-source license, and incompatible with GPL/MIT. The bundled
`AcknexCSApi` fork claims MIT on GitHub but has **no LICENSE file in the local
copy**, and it derives from firoball's work whose license is unclear. **Do not
copy code from these into the distributed engine.** Use them only as *local
tools* and as *behavioral references* under a clean-room process. Full analysis:
[`04-licensing-and-legal.md`](04-licensing-and-legal.md).

## Toolchain status on this machine

Already installed and current: **.NET SDK 10.0.301** (plus 6/8), **git 2.54**,
**winget**. The provided research material (game + patch + reference code, ~240 MB)
is nested in the git-ignored `_research/` folder. See
[`05-tooling-and-environment.md`](05-tooling-and-environment.md) for what to add.

## Recommended path (detail in [`06-reimplementation-strategy.md`](06-reimplementation-strategy.md))

1. **Asset pipeline first.** Write our own readers for `WRS`, `WMP`, `WDL`,
   `MDL`, `PCX`, sprites — informed by published format docs, not by copying GPL/NC code.
2. **WDL interpreter, not transpiler.** Reimplement the WDL runtime semantics
   (REGION/WALL/THING/ACTOR/WAY objects, SKILLs, ACTIONs, the `each_cycle` tick
   model) as an interpreter over the original game's scripts.
3. **2.5D renderer on Direct3D 11** via Vortice.Windows.
4. **Validate against the DOSBox-X oracle** continuously.

## Open decisions for the project owner

- **Engine license** (recommend GPLv3 — OpenMW model).
- **Project scope:** Saints-only, or a reusable Acknex-3 runtime with a Saints
  "game profile" (recommended — it's barely more work and matches the prior art).
- **Renderer backend:** Direct3D 11 (recommended for parity speed) vs. 12.
- Whether to **contact firoball/TheSameCat2** to request permissive relicensing —
  could save enormous effort if granted.
