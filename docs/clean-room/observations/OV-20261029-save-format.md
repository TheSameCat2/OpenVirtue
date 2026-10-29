# OV-20261029-save-format: the SAVE and SAVE_INFO files
Date: 2026-10-29
Status: SAVE_INFO confirmed and implemented; full SAVE partly read
Observer: OpenVirtue maintainers (black-box probe and byte comparison)
Implementer: OpenVirtue maintainers

## Question
What files do `SAVE` and `SAVE_INFO` write, how are they encoded, where do the skills live, and what
does `LOAD_INFO` bring back? (Retail-save compatibility is a roadmap goal.)

## Why It Matters
OpenVirtue ignored `SAVE_INFO`/`LOAD_INFO` and wrote full saves as its own JSON snapshots, so it could
neither read the original's saves nor write ones the original reads.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); the save files it wrote and
  file-output trace markers. The byte analysis is our own (see `src/OpenVirtue.Formats/PROVENANCE.md`).
- Reference material consulted: the 1998 A3 manual p.94 (`SAVE`, `LOAD`, `SAVE_INFO`, `LOAD_INFO`),
  p.111 (skill `TYPE`: `GLOBAL` in both, `LOCAL` only in `SAVE_INFO`, `PLAYER` only in `SAVE`), p.60
  (`SAVE`/`SAVE_ALL` region flags).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-save-format/`.
- Relevant local-only file hashes (sha256, first 8): patched `VRUN.EXE` `bba9f9bc`; `OVSAV1.SAV`
  info-1 `ca71db33`, info-2 `f1d2da16`, info-3 `fcd2ed11`; `OVSAV2.SAV` full-1 `15c3585d`, full-2
  `3eb9efb3`; roundtrip-info `OVSVF0.TXT` `ce8e4967`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only; no save file
  is committed.

## Setup
Native Linux DOSBox-X 2026.10.01; carriers from `ovtool oracle prepare save-format`
(`tools/OpenVirtue.Tools/SaveFormatProbeCases.cs`): skills `sv_g` (`TYPE GLOBAL`), `sv_l`
(`TYPE LOCAL`), `sv_p` (default `PLAYER`), `SAVE_INFO "ovsav", 1` or `SAVE "ovsav", 2`; round trips
change the values after saving and load them back.

## Observations
| Item | Evidence | Reading |
|---|---|---|
| file name | `OVSAV1.SAV`, `OVSAV2.SAV` in the game directory | the base in upper case, the number unpadded (like `PRINTFILE`, OV-20261029-print-output) |
| container | first 8 bytes big-endian 64 and 82 for a 72-byte `SAVE_INFO` file (2400 and 8763 for a full save); the rest decompresses with the WRS LZSS to exactly the second size | the WRS entry framing without the name |
| `SAVE_INFO` body | little-endian: type word `0x186`, body length (82), a section `[size 38][own offset 12][count 2][records]`, two sections `[size 4][own offset][count 0]`, end marker `25 26 00 00` | |
| skill record | `sv_l\0` + `00 34 01 00` + `01 00 00 80` + `ff ff ff 7f` | name, NUL, raw value (77·1024), raw minimum and maximum (no `MIN`/`MAX`: ±2097151.999) |
| order | `sv_l` before `sv_g` though declared after | reverse declaration order |
| `GLOBAL` 1234.5 → 4321.25 | only the value bytes change (`00 4a 13 00` → `00 85 43 00`) | |
| `LOCAL` 77 → 99, `PLAYER` 5 → 6 | only `sv_l`'s value changes | `SAVE_INFO` keeps `GLOBAL` and `LOCAL`, not `PLAYER` |
| full `SAVE` body | starts with type `0x69a` and the world's `TITLE.WDL`, `TITLE.WMP`, `TITLE.WRS`; a skill section `[size][own offset][count 240]` holds `sv_p`, `sv_g` and the predefined skills (`PLAYER_X` 0x1400 = 5.0 …), not `sv_l`; much else is binary world state | `SAVE` keeps `GLOBAL` and `PLAYER` skills |
| roundtrip-info | `V 1234.5 77 5`, set all to 1, `LOAD_INFO`, `LOADED`, `V 1234.5 77 1`, `RET` | `LOAD_INFO` restores `GLOBAL` and `LOCAL` in the instruction; the action goes on |
| roundtrip-full | `SAVE` written (`OVSAV4.SAV`); after `LOAD` the run never reached `EXIT` (timeout) and the buffered trace was lost | not determined |

## Invariant
A save file is `[u32 BE compressed size][u32 BE body size][LZSS]`. A body is little-endian; it holds
sections `[u32 size][u32 own offset][payload]`, a skill section's payload being a count and records
of name, NUL, raw value, raw minimum, raw maximum. `SAVE_INFO` writes type `0x186`, the body length,
the `GLOBAL` and `LOCAL` skills (reverse declaration order), empty `FREEZE`-bitmap and `INKEY`-string
sections and `25 26 00 00`. `LOAD_INFO` sets those skills back at once.

## Open Questions
- The rest of a full `SAVE` (regions, walls, objects, actions, waits) and what `LOAD` does with a
  running action.
- The sections `SAVE_INFO` writes for `FREEZE` bitmaps and `INKEY` strings when there are some.

## Implementation
`OpenVirtue.Formats.Save.AcknexSaveFile` (pack/unpack, `WriteInfo`, `ReadSkills`);
`WdlRuntime.SaveGame`/`LoadGame` write and read `SAVE_INFO` files in `SaveDirectory` (the host's
content root); a full `LOAD` without an OpenVirtue snapshot imports an original save's `GLOBAL` and
`PLAYER` skills and so the player's position (`RuntimeHost.ImportOriginalSave`). Full `SAVE`s are
still OpenVirtue's JSON. `ovtool oracle diff` compares save bodies: the `SAVE_INFO` ones match.
