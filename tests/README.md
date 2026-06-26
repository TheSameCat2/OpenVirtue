# tests

Automated tests (xUnit). See [../docs/recon/06-reimplementation-strategy.md](../docs/recon/06-reimplementation-strategy.md).

| Project | Covers |
|---------|--------|
| `OpenVirtue.Formats.Tests` | LZSS decoder, WRS archive reader, PCX reader, WAV reader, and the WDL lexer/parser/preprocessor and WMP map reader. |
| `OpenVirtue.Engine.Tests` | The hybrid object model, `LevelLoader`, the WDL expression/statement interpreter and `WdlRuntime`, and the geometry pipeline (ear clipping, mesh builder, texture loader). |

Tests use small, hand-built byte vectors so they run anywhere. Some tests
additionally validate against **real, user-supplied** game data if it is present
under the git-ignored `_research/` folder, and are a no-op otherwise — no game
data or fixtures are ever committed.
