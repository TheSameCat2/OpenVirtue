# src

Engine source. The realized layering (it differs from the early sketch in
[../docs/recon/06-reimplementation-strategy.md](../docs/recon/06-reimplementation-strategy.md)):
`Formats` depends on nothing, `Engine` depends on `Formats`, `Host` depends on `Engine`,
and only `App` pulls in SDL and OpenGL — so the engine stays headless-testable.

| Project | Depends on | Purpose |
|---------|-----------|---------|
| `OpenVirtue.Formats` | — | Pure, dependency-free readers for the game's formats: WRS (LZSS) archives, PCX images, WAV, and the WDL lexer/parser/preprocessor and WMP maps. No graphics. |
| `OpenVirtue.Engine` | Formats | World model (the hybrid object model of ADR-0006), the WDL interpreter (`Interpreter/`), level loading (`LevelLoader` → `Level`), and GPU-agnostic geometry/texture decoding (`Rendering/`). No graphics API. |
| `OpenVirtue.Host` | Engine | Frame loop, input skills, eight-voice mixer, and PNG screenshots. No native dependencies; tests use fakes. |
| `OpenVirtue.App` | Host | The SDL3 + OpenGL 3.3 windowed level viewer. |
| `legacy/OpenVirtue.App.D3D11` | Engine | The previous Direct3D 11 viewer. Windows-only, not extended, deleted once the GL host has replaced it. On other platforms the project is an empty library so the solution still builds. |

Note where things actually live: the WDL interpreter is in `OpenVirtue.Engine`
(there is no separate `OpenVirtue.Wdl` project), and rendering is split — GPU-agnostic
mesh/texture work in `Engine/Rendering`, the frame loop in `Host`, and the GL device in `App`.

Each non-trivial format reader carries a clean-room provenance note —
see [`OpenVirtue.Formats/PROVENANCE.md`](OpenVirtue.Formats/PROVENANCE.md).
