# ADR-0003: Engine license — GPLv3 (or-later)
Date: 2026-06-25
Status: accepted

## Context
OpenVirtue is a fan-preservation engine reimplementation in the OpenMW model.
We need a license that keeps the engine and its derivatives open, is OSI-approved,
and is compatible with our dependencies (.NET runtime = MIT, Vortice.Windows =
MIT — both GPL-compatible).

## Decision
License the project under **GNU GPL v3.0-or-later**. The canonical text is in
`/LICENSE`. New source files should carry an SPDX header:

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors
```

Copyright is attributed to "**The OpenVirtue Authors**" (contributors collectively).

## Alternatives considered
- **MIT / Apache-2.0:** maximally permissive; rejected to keep derivatives open
  in the OpenMW spirit (project owner's preference).
- **GPL-3.0-only:** pinned to v3; we chose **-or-later** to allow adopting future
  FSF versions without relicensing.

## Consequences
- CC-BY-NC reference code (firoball's WDL2CS/WMPio) **cannot** be incorporated —
  reinforces the clean-room, reference-only policy (see ADR-0005).
- Contributors implicitly license contributions under GPLv3-or-later; consider a
  short `CONTRIBUTING.md`/DCO note when the project opens to outside contributors.
