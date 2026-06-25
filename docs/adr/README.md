# Architecture Decision Records

Short, dated records of significant design decisions and their rationale.
One file per decision: `NNNN-short-title.md`.

Suggested template:

```
# ADR-NNNN: <title>
Date: YYYY-MM-DD
Status: proposed | accepted | superseded by ADR-XXXX

## Context
What problem/decision, what constraints.

## Decision
What we chose.

## Alternatives considered
What else, and why not.

## Consequences
Trade-offs, follow-ups, risks.
```

## Decisions on record

| ADR | Decision | Status |
|-----|----------|--------|
| [0001](0001-renderer-direct3d11-vortice.md) | Renderer: **Direct3D 11** via Vortice.Windows | accepted |
| [0002](0002-wdl-interpreter-not-transpiler.md) | WDL **interpreter**, not transpiler | accepted |
| [0003](0003-license-gplv3.md) | License: **GPL-3.0-or-later** | accepted |
| [0004](0004-project-name-openvirtue.md) | Project name: **OpenVirtue** | accepted |
| [0005](0005-clean-room-reference-only-policy.md) | **Clean-room, reference-only** use of prior art | accepted |
| [0006](0006-engine-object-model-hybrid.md) | Engine object model: **hybrid** typed entities + dynamic skills | accepted |
