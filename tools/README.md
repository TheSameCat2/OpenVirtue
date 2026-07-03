# tools

Local inspection/extraction utilities built on `OpenVirtue.Formats`. These ship
no game data; they operate on the user's own files.

## OpenVirtue.Tools (`ovtool`)

```
dotnet run --project tools/OpenVirtue.Tools -- wrs list    <archive.wrs>
dotnet run --project tools/OpenVirtue.Tools -- wrs extract <archive.wrs> [output-dir]
dotnet run --project tools/OpenVirtue.Tools -- wrs pack    <input-dir> <archive.wrs>
dotnet run --project tools/OpenVirtue.Tools -- pcx info     <image.pcx>
dotnet run --project tools/OpenVirtue.Tools -- wmp info     <map.wmp>
dotnet run --project tools/OpenVirtue.Tools -- wav info     <sound.wav>
dotnet run --project tools/OpenVirtue.Tools -- wdl info     <script.wdl>
dotnet run --project tools/OpenVirtue.Tools -- level info   <archive.wrs> [main.wdl]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare scheduler-if-start-cycle [output-dir] [--runtime-dir <dir>] [--carrier-dir <dir>] [--dosbox-x <exe>]
```

- `wrs list` — print each entry (name, compressed/uncompressed size) and a
  summary by file type.
- `wrs extract` — decompress every entry to a folder.
- `wrs pack` — build a local WRS archive from a flat directory using
  deterministic literal-only LZSS payloads.
- `pcx info` — print a PCX image's dimensions and a palette sample.
- `wmp info` — print a map's vertex/region/wall/thing/actor counts and player start.
- `wav info` — print a sound's channels/rate/bit-depth and duration.
- `wdl info` — parse a script and summarize its top-level items by keyword.
- `level info` — load a whole level (WDL + WMP) and report its geometry, placements, and skills.
- `oracle prepare scheduler-if-start-cycle` — create a local-only DOSBox-X probe
  folder under `_research/oracle-runs/` for measuring `IF_START` versus first
  `EACH_TICK` dispatch. Generated WDL/WMP/WRS/runtime/display-asset files are
  ignored and must not be committed; reduce results into
  `docs/clean-room/observations/`.
  Passing `--carrier-dir` also creates a local prepared-title carrier harness
  with active `APPEND_MODE` lines removed from copied WDL files and marker-file
  logging injected into the title script. Marker files use the A3 documented
  five-character base plus number form; patched V3.963 has been observed writing
  one-digit suffixes such as `OVSTA0.TXT`, `OVSCH0.TXT`, and `OVTIK0.TXT`.
  This helper does not run SaintsX setup scripts or external `patch.exe`; oracle
  prep must stay noninteractive and auditable.
