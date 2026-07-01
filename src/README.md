# src

Engine source. The realized layering (it differs from the early sketch in
[../docs/recon/06-reimplementation-strategy.md](../docs/recon/06-reimplementation-strategy.md)):
`Formats` depends on nothing, `Engine` depends on `Formats`, and only `App` pulls in
Direct3D — so the engine stays headless-testable.

| Project | Depends on | Purpose |
|---------|-----------|---------|
| `OpenVirtue.Formats` | — | Pure, dependency-free readers for the game's formats: WRS (LZSS) archives, PCX images, WAV, and the WDL lexer/parser/preprocessor and WMP maps. No graphics. |
| `OpenVirtue.Engine` | Formats | World model (the hybrid object model of ADR-0006), the WDL interpreter (`Interpreter/`), level loading (`LevelLoader` → `Level`), and GPU-agnostic geometry/texture decoding (`Rendering/`). No graphics API. |
| `OpenVirtue.App` | Engine | The Direct3D 11 (Vortice) windowed level viewer with first-person debug movement — the only project that references DirectX. |

Note where things actually live: the WDL interpreter is in `OpenVirtue.Engine`
(there is no separate `OpenVirtue.Wdl` project), and rendering is split — GPU-agnostic
mesh/texture work in `Engine/Rendering`, the actual D3D11 device and render loop in `App`.

Each non-trivial format reader carries a clean-room provenance note —
see [`OpenVirtue.Formats/PROVENANCE.md`](OpenVirtue.Formats/PROVENANCE.md).
