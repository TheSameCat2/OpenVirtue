# tests

Automated tests (xUnit). See [../docs/recon/06-reimplementation-strategy.md](../docs/recon/06-reimplementation-strategy.md).

| Project | Covers |
|---------|--------|
| `OpenVirtue.Formats.Tests` | LZSS decoder and WRS archive reader. |

Tests use small, hand-built byte vectors so they run anywhere. Some tests
additionally validate against **real, user-supplied** game data if it is present
under the git-ignored `_research/` folder, and are a no-op otherwise — no game
data or fixtures are ever committed.
