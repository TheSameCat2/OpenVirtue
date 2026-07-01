# Reconnaissance findings

Research conducted 2026-06-25 (research phase — no engine code written yet).

| Doc | Contents |
|-----|----------|
| [00-overview.md](00-overview.md) | Executive summary & the headline findings. |
| [01-engine-and-game.md](01-engine-and-game.md) | The game, Acknex-3 lineage, WDL object/execution model, known quirks. |
| [02-file-formats.md](02-file-formats.md) | WRS/WDL/WMP/MDL/PCX/WAV/etc. — what to reverse, in what order. |
| [03-prior-art.md](03-prior-art.md) | firoball, rickomax, TheSameCat2, SaintsX — and how they differ from us. |
| [04-licensing-and-legal.md](04-licensing-and-legal.md) | **Read this.** Clean-room rules, CC-BY-NC problem, EULA, trademarks, ethics. |
| [05-tooling-and-environment.md](05-tooling-and-environment.md) | What's installed, what to install, why Vortice/Direct3D. |
| [06-reimplementation-strategy.md](06-reimplementation-strategy.md) | Interpreter-not-transpiler, architecture sketch, milestone roadmap, risks. |

All original game data, the SaintsX fan patch, and third-party reference code live
in the git-ignored `_research/` folder for local study only — never committed,
never redistributed.

For current implementation status, prefer the root README. For hidden runtime
behavior that must be learned from the original engine, follow
[`docs/clean-room/oracle-protocol.md`](../clean-room/oracle-protocol.md) and
record sanitized observations under `docs/clean-room/observations/`.
