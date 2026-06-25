# ADR-0002: WDL execution — interpreter, not transpiler
Date: 2026-06-25
Status: accepted

## Context
Acknex-3 games are driven by WDL scripts (many modules per world). We must
execute the user's *own* WDL at runtime. The main prior art (firoball's WDL2CS)
**transpiles** WDL → C# at build time and runs it under Unity.

## Decision
Implement a **WDL interpreter** in `OpenVirtue.Wdl`: lex/parse the user's scripts
to an AST and execute them against our engine's object/command model (skills,
actions, the `each_cycle`/scheduler tick).

## Alternatives considered
- **Transpile WDL → C# (firoball's approach):** requires per-game code generation,
  produces derived-code artifacts with messy licensing/provenance, and couples us
  to a host language's semantics for behaviors we need to match exactly.

## Consequences
- Loads any game's scripts at runtime; no generated per-game code to license.
- Cleanest OpenMW-style separation (engine ships; user supplies scripts).
- Easier to match fixed-tick timing and skill arithmetic precisely (we are the
  runtime).
- Interpreter performance is a non-issue for A3-era logic; a transpile/JIT path
  can be added later behind the same interface if profiling ever demands it.
