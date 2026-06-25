# ADR-0006: Engine object model — hybrid typed entities + dynamic skills
Date: 2026-06-25
Status: accepted

## Context
The engine must represent Acknex objects — REGIONs, WALLs, THINGs, ACTORs,
vertices — for the world model and, later, the WDL interpreter runtime. WDL is
highly **reflective**: scripts read/write properties dynamically (`my.skill`,
`you._x`), define custom skills (globals.wdl alone declares ~233 `SKILL`s), and
alias them with `SYNONYM`/`DEFINE`. But the common geometric/entity fields
(positions, heights, angles, texture refs) are well-known and fixed.

Two extremes were considered: a **fully dynamic property-bag** (every field is a
dictionary entry — maximally faithful, less type-safe) and **strongly-typed
classes** for every documented field (the approach of the project owner's
`AcknexCSApi` — best tooling, but awkward for arbitrary/custom skill access).

## Decision
Use a **hybrid** model:

- **Typed entity classes** (`Region`, `Wall`, `Thing`, `Actor`, `Vertex`, …) carry
  the well-known fields as real properties, for clarity and speed.
- **A per-object dynamic skill/property table** (case-insensitive name → value)
  backs WDL's reflective access, custom skills, and synonyms.
- A **reflective accessor** (`obj["name"]`) resolves well-known names to the typed
  fields and everything else to the dynamic table, so the interpreter can treat
  all property access uniformly.

## Consequences
- Clean, fast access to common fields; full fidelity for WDL's dynamic semantics.
- The interpreter gets a single uniform `get/set property by name` path.
- Slightly more plumbing than either extreme (the reflective accessor must bridge
  typed fields and the dynamic table) — accepted.
- Lives in a new `OpenVirtue.Engine` project (depends on `OpenVirtue.Formats`).
- Synonyms/defines resolve to skill names at load time; the dynamic table is the
  source of truth for non-typed values.
