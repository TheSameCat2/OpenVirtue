# ADR-0005: Clean-room, reference-only use of prior art
Date: 2026-06-25
Status: accepted

## Context
The best Acknex-3 references (firoball's WDL2CS, WMPio, AcknexCSApi) are
license-incompatible with a GPLv3 project: WDL2CS and WMPio are **CC BY-NC 4.0**
(NonCommercial, not OSI, GPL-incompatible); AcknexCSApi's licensing is unsettled.
The project owner (GitHub: **TheSameCat2**, maintainer of the bundled AcknexCSApi
fork) reports firoball appears to have **abandoned** the upstream effort, so
relicensing-by-request is not a reliable path. Decision needed on how we may use
all prior art.

## Decision
Treat **all** material under `_research/` (the original game, the SaintsX patch,
and every third-party reference repo) as **read-only reference under a clean-room
process**. We **do not copy code** from it into OpenVirtue. We extract *facts* —
format layouts, API/behavior descriptions — restate them in our own words in
`docs/`, and implement from `docs/` and from our own byte/behavior analysis.

Concretely:
1. `_research/` stays **git-ignored**; nothing from it is committed verbatim.
2. No copy-paste from CC-BY-NC, unlicensed, or decompiled sources — ever.
3. Each non-trivial format reader carries a `PROVENANCE` note: how we learned the
   layout (published doc URL, our own analysis, behavioral test vs. the DOSBox-X
   oracle) — explicitly *not* "ported from <repo>".
4. Attribute ideas/credit in docs and README without importing expression.

## Alternatives considered
- **Request permissive relicensing from firoball:** preferred in the abstract,
  but upstream is abandoned → unreliable. Not pursued.
- **Adopt firoball's code directly:** impossible under GPLv3 (NC incompatibility)
  and would forfeit clean provenance.

## Consequences
- Clean, defensible IP provenance for a public GPLv3 release.
- More implementation work (we rebuild parsers/readers ourselves) — accepted as
  the cost of a legally clean engine.
