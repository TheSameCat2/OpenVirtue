# src

Engine source. See [../docs/recon/06-reimplementation-strategy.md](../docs/recon/06-reimplementation-strategy.md)
for the planned layering.

| Project | Status | Purpose |
|---------|--------|---------|
| `OpenVirtue.Formats` | **in progress** | Pure, dependency-free readers for the game's binary/text formats. Started with WRS (LZSS) archives. |

Each non-trivial format reader carries a clean-room provenance note —
see [`OpenVirtue.Formats/PROVENANCE.md`](OpenVirtue.Formats/PROVENANCE.md).

Planned next: `OpenVirtue.Wdl`, `OpenVirtue.Engine`, `OpenVirtue.Rendering`
(Direct3D 11 / Vortice), `OpenVirtue.Platform`, `OpenVirtue.App`.
