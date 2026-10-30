# OV-20261029-save-body: where a full SAVE keeps the world's objects
Date: 2026-10-29
Status: confirmed (six rounds; three runs crashed on an `AMBIENT` write) and implemented for reading
Observer: OpenVirtue maintainers (black-box probe, byte comparison)
Implementer: OpenVirtue maintainers

## Question
OV-20261029-save-format read a full `SAVE`'s container, its header and its skill section. Which
sections hold the regions, walls, things, actors, global slots and waiting actions? How are their
records laid out?

## Why It Matters
Loading the original's saves, and later writing saves it can load, needs the world state, not only
the skills.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); the `.SAV` files it wrote,
  unpacked with `AcknexSaveFile.Unpack`.
- Reference material consulted: the 1998 A3 manual p.94 and p.111 (`SAVE`, `LOAD`, `SAVE_ALL`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-save-body/`. Round 1 is at the top level,
  rounds 2–6 are in `roundN/`.
- Relevant local-only file hashes (sha256, first 8), `OVSAV2.SAV`: base `415e7841`, slot
  `c4f904f5`, actor-fields `0a466823`, thing-a `04c2d252`, wall-b `cb4e9931`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Emulator: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`. The carriers come from
  `ovtool oracle prepare save-body` (`tools/OpenVirtue.Tools/SaveBodyProbeCases.cs`).
- World: every case builds the same world. It has the room's 12 walls, a free-standing wall `sb_w`
  (last in the WMP), a thing `sb_t` and an actor `sb_a`.
- Round 1: each case changes one value and saves `OVSAV2.SAV`; the unpacked bodies are compared
  byte by byte with `base`'s.
- Rounds 2–6: each case sets many skills of one object, each to its own raw pattern `0x201 + 64·i`.
  The patterns are then searched for in the bytes.

## Observations
- **Section layout.**
  - The world above gave 13 sections: an 80-byte block, two empty sections, then 2654, 200, 4,
    672, 96, 5363 (the skills), 44, 4, 4 and 0 bytes.
  - The save-format world (12 walls, no things) had 2136 and 84 bytes where this one has 2654
    and 96.
  - 2136 = 12 × 178, and 2654 = 2 × 170 + 13 × 178.
- **Object section (the fourth).**
  - It holds the actor's record, then the thing's, then the walls'. The first wall record is
    `sb_w`, the last wall in the map: reverse map order, as the scheduler visits objects.
  - Thing and actor records place the fields identically, at these offsets:

    | Offset | Field |
    |---|---|
    | +8 | `ANGLE` |
    | +12, +16 | `TARGET_X`, `TARGET_Y` |
    | +36, +40, +44 | `HEIGHT`, `SPEED`, `VSPEED` |
    | +52 | `DIST` |
    | +60 to +88 | `SKILL1` to `SKILL8` |
    | +102 | `CYCLE` |
    | +110, +114, +118 | X, Y, Z |

  - Wall records hold `OFFSET_X` at +12, `OFFSET_Y` at +16, `DIST` at +52 and `SKILL1`–`SKILL4`
    at +60…+72.
- **Regions are not saved.**
  - Setting the room's `FLOOR_HGT`, `CEIL_HGT` or `SKILL1` changed no object or region bytes.
  - The heights changed only the player skills `FLOOR_HGT` and `CEIL_HGT` in the skill section.
    `SKILL1` changed nothing but bytes that also differ between two saves of one world.
- **Global slots.** Setting `EACH_TICK.3` changed 4 bytes of the 672-byte section (168 words)
  from −1 to 12, at word 141. So that section holds the action slots, as action numbers, and
  `EACH_TICK.1` is word 139 (corrected in round 9: `EACH_TICK.n` is word 135 + 2n).
- **Action numbers.** Actions are numbered from 11 in declaration order: `probe_start` was 11 when
  it was the only action, and 13 after `sb_wait` (11) and `sb_slot` (12). Numbers 0–10 are the
  engine's own actions. The base slot table holds them at words 11–13, 15, 27, 53, 60, 61, 63, 64,
  68 and 88 (likely default key hooks).
- **Running and waiting actions.** The 44-byte section (the tenth) grew to 68 bytes with a
  `CALL`ed action waiting in `WAITT 200`. Each action is a 7-word record:
  - a 1;
  - the action number;
  - the remaining wait as a raw value (204000 = 199.22 ticks of `sb_wait`'s `WAITT 200`);
  - two words that look like instruction positions (57, 58 for `probe_start` at its `SAVE`; 58, 59
    with one more instruction before it; 0, 0 for `sb_wait` at its first instruction);
  - `MY` and `YOU` (−1).

  The list ends with a 0. The words before the first record were 0, 0, 0 in `base` and 0, 0 in
  `wait`, so the header is not settled.
- **Volatile bytes.** `base` and `base2` differ only in a few bytes of the skill section.
- **`AMBIENT` crash.** Writing `AMBIENT` on a thing or wall crashed the original at the
  instruction ("Engine crash at SET DO_ACTION"). On an actor it was accepted but not saved.
  `ASPEED` and `MAP_COLOR` are not object skills in A3 (Genesis "Error(50): Parameter unknown").

## Invariant
- A full `SAVE`'s fourth section is the object section: one 170-byte record per actor and thing,
  then one 178-byte record per wall, each kind in reverse map order. The fields are at the
  offsets above, as raw 22.10 values.
- Region state is not saved: a load keeps the level's region values.

## Round 7: loading (2026-10-30)
Carrier `load-edit`, run several times on one work directory. It sets `EACH_TICK.2` to a sampler that
appends a line every cycle (`TICKS`, its count `sb_k`, the `LOCAL` skill `sb_m`, `sb_n`, the actor's
X), waits 2 cycles, then runs `LOAD "ovsav", 2; WAIT 3; sb_m = 2; sb_n = 7; CALL sb_note; SAVE
"ovsav", 2;` and prints. `sb_note` waits 2 cycles and prints `N` with `sb_k` and `sb_m`. Hashes
(run 2): `OVSVT1.TXT` `6c2ad2d1`, `OVSVN0.TXT` `cb95a2ea`.

| Run | Observed | Reading |
|---|---|---|
| 1 (no save yet) | the action goes on past `LOAD`; it prints `L 2 7 …` and `N 7 2` | a `LOAD` of a missing file does nothing |
| `LOAD` then straight on to `SAVE` | run 1's values again, and the file is rewritten | the `LOAD` had not happened yet: it is deferred to the end of the cycle |
| 2 (`WAIT 3` after `LOAD`) | cycles 1–3 as before; from the next the sampler reads `sb_k` 6, `TICKS` 15.188, `sb_n` 7, `sb_m` 0; `N 6 0`; no `L` line; the run never ends | the save's world, slots and skills come back. `LOCAL` skills keep their current values. The `CALL`ed waiting action resumes, but the action that ran `SAVE` does not |
| 3 (the save edited: actor X 25, `sb_n` 5, repacked by `ovtool save pack` as literal-only LZSS) | the sampler reads `sb_n` 5 and X 25 after the load | the original loads an edited save: no checksum, and OpenVirtue's packing is accepted |

The save written by run 1 lists three action numbers in its 96-byte seventh section: 13, 15 and 14,
the sampler, the probe action and `sb_note` (actions count from 11). The tenth section holds two
7-word records, `[1][15][0][ip][ip][−1][−1]` and `sb_note`'s.

Object records also hold, besides the fields above:
- pointer-like pairs, such as walls' +28/+30 `−23568, 90`;
- flags at +20/+22;
- `16383` at +0 and +4, and `−255` at +92;
- runs of −1 from +130 or +146.

Writing a save from scratch needs these understood.

## Round 8: the action records (2026-10-30)
Carriers `ip`, `ip2`, `ip3`. Each `CALL`s several actions that run different instructions before
`WAITT 100`, then `WAIT 3` and `SAVE`. Hashes of `OVSAV2.SAV`: ip `ca5db9f4`, ip2 `f238a904`, ip3 `2cf70971`.

The tenth section is `[0][0][n]`, then n six-word records for the waiting actions,
`[action][remaining raw][place][next][MY][YOU]`, then one seven-word record per action that was
running, `[1][action][…]`, and a closing 0.

| Action body (the wait is `WAITT 100`) | place, next |
|---|---|
| `WAITT` | 0, 0 |
| `RULE; WAITT` (and `SET`, `PRINTFILE`, `PRINT_STRING`, `PRINT_VALUE`, a long expression, `WAIT 1` in place of the `RULE`) | 1, 0 |
| `RULE; RULE; WAITT` | 2, 0 |
| `WAITT; RULE` (one to three `RULE`s after) | 0, 1 |
| `RULE; WAITT; RULE` | 1, 2 |
| `IF (…) { RULE } WAITT` | 2, 0 |
| `IF (…) { RULE } ELSE { RULE } WAITT` | 4, 0 |
| `IF (…) { WAITT } RULE` | 1, 2 |
| `WHILE (…) { WAITT }`, with or without a `RULE` after the loop or the wait | 1, 2 |
| `WHILE (…) { RULE; WAITT }`; `RULE; WHILE (…) { WAITT }` | 2, 3 |
| the action running `SAVE`, the 67th instruction counting from 0 | 65, 66 |

- **The place.** The waiting instruction's index when the action's instructions are counted in
  order, each taking one, with `IF` and `ELSE` bodies in line.
- **Next.** The place after it, or 0 when it is the action's last. A `WHILE` body is followed by a
  place of its own for the loop's end.
- **The running action.** It records the place before the `SAVE` and the `SAVE`'s own.
- `MY` and `YOU` read −1 for these global actions; their form for object actions is unread.

Implementation:
- `AcknexSaveFile.ReadActions` and `ReadSlots` read the records and the slot words.
- `WdlRuntime.ImportOriginalActions` maps action numbers through the declaration order
  (`Level.ActionOrder`, from 11) and places through `WdlFlatPosition`. It replaces the waiting
  actions and the `EACH_TICK` slots, and skips the running action.
- `RuntimeHost.ImportOriginalSave` now loads them with the skills and objects.
- Tests: `Runtime_ImportOriginalActions_ResumesAtTheSavedFlatPlaces`, and the guarded
  `ReadActions_TheIpProbesSave_ListsTheWaitingActionsThenTheSavingOne`.

## Round 9: which bytes a load needs (2026-10-30)
Run 1 of `load-edit` made a fresh save. Each variant copied it with the actor's X set to 25 and
`sb_n` to 5 (to see the load), blanked some bytes, repacked it and loaded it in a second run.
"Loads" means the sampler showed 25 and 5 afterwards and `sb_note` resumed. "Crash" means the
original quit without its end marker, the buffered traces lost.

| Blanked | Result |
|---|---|
| nothing | loads |
| section 0 (80 bytes) | loads (`TICKS` came back a little different) |
| sections 4 and 7 set to −1 | loads, but `sb_as.X` reads 0: section 7 (`13, 15, 14, −1 …`) seems to hold the three `SYNONYM`s' objects |
| every unread byte of every thing, actor and wall record | crash |
| thing and actor records: +0…+7, +92, +128…+169, +22…+29, one range at a time | loads |
| thing and actor records: +20 (`0x627` actor, `0x607` thing) | crash |
| wall records: +0…+11, +18…+35, +90…+99, +110…+129 (the wall's vertices, recomputed), +130…+177, one range at a time | loads |
| wall records: +100…+109 (+102 holds 1.0, as in thing and actor records) | crash |
| every unread byte except +20 and +102 | crash |
| cumulatively: the thing and actor ranges, then wall +0…+11 and +18…+35 | loads; adding wall +90…+99 crashes |

Later variants (copies of the carrier run six at a time):
- **Record fields.** Writing the records as a writer would loaded: pointers zeroed, flags
  normalised, vertices zeroed. The varying wall fields read:
  - +8: the length;
  - +0: the length × the texture's `SCALE_X`;
  - +142: the texture's number (from 1, in declaration order; +134 for things and actors);
  - +138: the right region;
  - +110…+128: the vertices.
- **Optional parts.** Sections 0 and 4, the header's word after the names, and the engine's default
  slot words can each be left out.
- **Synonyms.** Section 7 holds the synonyms' objects in reverse declaration order, the level's first,
  then 21 predefined ones. An object's number is its place among the map's walls, things and actors
  plus 1 (12 read room wall #11, 13 `sb_w`, 14 the thing, 15 the actor).
- **Skills.** They are matched by name: a reversed section loads. A section with only the level's
  six skills is not loaded at all, so every one of the engine's 238 own skills must be there.
- **Actions.** The record of the action running the `SAVE` must be there, but any action at any place
  will do.
- **Slots.** `EACH_TICK.n` is slot word 135 + 2n (`.2` word 139, `.3` word 141), not 138 + n as
  round 1 assumed.
- **Waits.** A wait is saved as it stands after the current cycle's count.

**OpenVirtue's save loads in the original.** OpenVirtue replayed `load-edit` and wrote
`OVSAV2.SAV` with `AcknexFullSave`. The original then loaded it: the sampler continued from
OpenVirtue's `sb_k` and `sb_n`, the `LOCAL` `sb_m` kept 0, and the waiting `sb_note` resumed
(`N 6 0`). Hashes in `round7/ov-written-load/`: the save `4837063a`, `OVSVT1.TXT` `a70e78fb`,
`OVSVN0.TXT` `cb95a2ea`.

The +20 word is `0x607` for things, `0x627` for actors, and `0x602` or `0x002` for walls in every
save. The `0x600` bits differ between walls in one save, so they look like view state. A writer
needs at least the +20 flags and +102 = 1.0. It also needs some of wall +90…+99 (−255 at +92) together
with other unread bytes, so it cannot write zeros for everything it does not understand. It writes
the constants the original's saves hold instead.

Implementation:
- `AcknexFullSave.Write` writes a body from a `FullSave`. `AcknexPredefinedSkills` lists the
  engine's skills.
- `WdlRuntime.CaptureOriginalSave` fills the `FullSave`. It uses `Level.MapObjects` (the map's
  object order, from `WmpMap.ObjectOrder`), `Level.ActionOrder`, `Level.MapFileName` and
  `WdlFlatPosition.TryPositionOf`.
- `RuntimeHost.WriteOriginalSaves` writes the original's file on a full `SAVE`, next to
  OpenVirtue's own save. It is off by default, and the oracle replay turns it on.
- The importer's slot words are corrected to 135 + 2n.
- Tests: `WriteFull_ReadsBackThroughTheReaders` and
  `Runtime_CaptureOriginalSave_RoundTripsThroughTheOriginalsFormat`.

## Round 10: `SAVE` regions (2026-10-30)
Retail levels flag regions `SAVE` (their doors, for instance). With the room declared `FLAGS SAVE`, a
full save gains a 156-byte record in its third section, which is empty otherwise. `region-fields` set
each region skill to its own pattern (hash `da519772`); the record is:

| Offset | Field |
|---|---|
| +0, +4 | `FLOOR_HGT`, `CEIL_HGT` |
| +8, +12 | `CEIL_OFFS_X`, `CEIL_OFFS_Y` |
| +16, +20 | `FLOOR_OFFS_X`, `FLOOR_OFFS_Y` |
| +24 | `AMBIENT` |
| +30, +32, +34 | 3, `0x702`, flags `0xf00` (`SAVE_ALL` adds `0x2000`) |
| +36, +44, +52, +60 | 1.0 each |
| +68…+96 | `SKILL1`…`SKILL8` |
| +100, +104 | the floor and ceiling textures' numbers |
| +108…+155 | −1 |

- **Synonyms.** A synonym naming a region holds the region's index (the room: 0), not a map place.
- **Load check.** `load-region` is `load-edit` with the room a `SAVE` region whose `SKILL1` is set to
  33 before the `SAVE`, and a region synonym sampled. The original loaded its own save and then
  OpenVirtue's: both read `SKILL1` 33 after the load. Hashes in `round10c/ov-written-load/`: the
  save `7b48040f`, the trace `f813ad2c`.
- **Not measured.** The order of several `SAVE` regions (written in reverse map order like the
  other kinds), and what `SAVE_ALL` adds for the region's walls and objects.

Implementation:
- `FullSaveRegion` and `AcknexFullSave.RegionFieldOffsets` for the writer, `ReadRegions` and
  `ReadSynonyms` for the reader;
- `CaptureOriginalSave` writes `SAVE` regions and region synonyms;
- the import applies region records and synonyms (`ImportOriginalSynonyms`).

`ovtool save capture` writes a level as loaded in this format. For the retail `apathy` level it gives
630 things and actors, 5894 walls, 29 `SAVE` regions, 650 skills (412 of the level's and 238 of the
engine's) and 85 synonyms (64 and 21). Loading such a save in the retail game is not tried yet:
- the game saves as `GAM1_` + the slot through its menu (`menu.wdl`);
- before its `SAVE` it sets `EACH_TICK.3` to reload its strings, relying on the slots being restored
  and the saving action not resuming;
- testing it needs a visible run driven through the title menu.

## Open Questions
- Whether actors and things are one list in reverse map order or grouped by kind. The probe had
  one of each; OpenVirtue reads actors first, as the scheduler visits them.
- The rest of each record (texture, flags, region, way state), the 200- and 96-byte sections,
  the action list's header, how the instruction positions count, and whether `SAVE_ALL`
  regions are saved. Resuming a saved action needs the instruction positions to match the
  original's.
- Wall `SKILL5`–`SKILL8` (not probed).

## Implementation
- `AcknexSaveFile.ReadObjects` reads the object section.
- `RuntimeHost.ImportOriginalSave` copies each record onto the level's actors, things and walls
  when a full `LOAD` finds only an original save.
- Tests: `AcknexSaveFileTests.ReadObjects_ReadsTheFourthSectionsEntityAndWallRecords`, and the
  guarded `ReadObjects_SavesTheDosRuntimeWrote_HoldTheMovedThing`.
- `ovtool save unpack|pack` turns a save into its body and back.
- `WdlRuntime.RestoreSkills` keeps `LOCAL` skills on a full `LOAD` (round 7).
- OpenVirtue's own full saves follow round 7's load behaviour:
  - `SaveGame` takes the state in the instruction, so the action running `SAVE` is left out;
  - the snapshot keeps the waiting actions, each as its action, its open blocks (item paths into
    the action), its wait and its objects;
  - `RestoreSnapshot` puts them in place of the ones waiting at the load.

  Tested by `Runtime_FullSave_KeepsWaitingActionsButNotTheOneSaving`.
- Writing full saves the original can load needs the remaining record fields and sections; the
  instruction positions are now known (round 8).
