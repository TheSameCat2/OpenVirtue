# ADR-0004: Project name — OpenVirtue
Date: 2026-06-25
Status: accepted (revisit freely — no code/namespaces depend on it yet)

## Context
We need a project name that signals "open reimplementation," is not a trademark
of the original, and doesn't imply endorsement by Shine Studios / Conitec.

## Decision
Use **OpenVirtue** as the project and solution name; C# root namespace
`OpenVirtue.*`. "Saints of Virtue" and "Acknex" are used only **nominatively**
("an open engine compatible with Saints of Virtue"), never as our product name.

## Alternatives considered (kept for reference)
- **OpenSaints** — clear, but leans harder on the game's trademark.
- **VirtueX** — echoes the SaintsX fan patch; risks confusion with that project.
- **Pilgrim / Reliquary** — thematic engine codenames; more abstract, less
  discoverable.
- **OpenAcknex / AcknexSharp** — engine-centric; better if scope becomes a
  general A3 runtime rather than a Saints-first one.

## Consequences
- Trivially renameable now (no code committed). **Before first public release:**
  do a trademark/availability check (GitHub org, domain, package id) and confirm.
