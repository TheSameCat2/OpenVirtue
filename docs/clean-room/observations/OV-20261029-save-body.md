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

## Round 11: a retail level (2026-10-30, partly)
The test level is a local, patched copy of the retail `apathy` level in `real-apathy/`; nothing from it
is committed.
- **Patches.** The DOS runtime rejects the retail scripts' two `SET APPEND_MODE` lines (V3.963 does not
  know the skill), so they are commented out. `start_game`'s `SAVE "START", 0` (the level-restart save
  the game reloads on death) is replaced by a `CALL`.
- **Loading OpenVirtue's save.** With a `LOAD` of OpenVirtue's capture of the level (`ovtool save
  capture`, a marker skill and a sampler slot set), the original loaded it and then crashed:
  `Engine crash at NEW_SIDE region prayerAltar2SideBelow`. Without the save file the patched level
  runs and the `LOAD` fails cleanly.
- **The original's own save.** With the `CALL`ed action doing `SAVE "ovorg", 1` it wrote one (hash
  `c063e44c`). Compared with OpenVirtue's capture:
  - the header keeps the map file's case (`apathy.WMP`);
  - the region section is the same size (29 × 156);
  - OpenVirtue listed 19 skills twice: the level declares them again, and the original lists each name
    once (633);
  - section 4 is 2804 bytes, not 200: a count (124), then records holding coordinates (round 13: screen
    elements, not ways);
  - the object section is 7900 bytes longer than 630 × 170 + 5894 × 178. No record carries `0x607` or
    `0x627` at +20 at a 170-byte stride. The +20 word is the object's flags, which the synthetic
    level's plain objects shared, not a kind marker.

Fixed: the duplicate skills and the name case. Still needed for retail levels:
- the flags word bit by bit (a probe setting one `FLAGS` at a time);
- the object section's extra data;
- section 4's way records.

`RuntimeHost.WriteOriginalSaves` stays off by default until then.

## Round 12: the flag words (2026-10-30)
29 carriers each declare one `FLAGS` on the thing, the actor and the free-standing wall (where the flag
applies), save, and are compared with `base` (hash of `flag-invisible`'s save: `5c091b1e`). Things and
actors start at +20 `0x607` (`0x627` for an actor) and +22 `0x180`. Walls start at +20 `0x602` and +22
`0x180`, plus `0x200` with one region on both sides.

| Flag | Thing / actor | Wall |
|---|---|---|
| `FLAG1`…`FLAG4` | +20 `0x1000`…`0x8000` | the same |
| `FLAG5`…`FLAG8` | +22 `0x1`…`0x8` | the same |
| `PASSABLE`, `BERKELEY`, `FAR` | +20 `0x100`, `0x800`, `0x80` | the same |
| `GROUND` / `PORTCULLIS`, `TRANSPARENT` | +20 `0x10` | +20 `0x10`, `0x4` |
| `INVISIBLE` | +20 loses `0x600`, gains `0x40`; +22 loses `0x80` | +20 loses `0x400`, gains `0x40` |
| `LIBER`, `CANDELABER` | +22 `0x40`, `0x400` | — |
| `FRAGILE`, `IMMATERIAL` | +22 `0x20`, `0x800` | the same |
| `SENSITIVE` | +22 `0x10`, and +24 holds 8 | the same |
| `IMPASSABLE` / `FENCE` | — | +22 `0x2000` / +22 gains `0x400`, loses `0x200` |
| `SAVE`, `SEEN`, `CURTAIN` | no change | no change |

`PLAY`, `MOVED`, `MASTER`, and `CAREFULLY` on a thing do not compile on this runtime ("Flag unknown").
The writer now encodes the flags. Its words match the original's in all 25 carriers that compiled,
for the thing, the actor and the wall.

Comparing one apathy wall and two still things with OpenVirtue's records of the same objects
(matched by position):
- **Walls.** +8 is the length cut toward zero (11448, where OpenVirtue rounded to 11449), and +0 is that
  length × `SCALE_X`. +142's texture number is 373 against OpenVirtue's 355. The original seems to count
  every `TEXTURE` declaration, repeats included, where OpenVirtue keeps one per name. +16 holds 1 and
  +134 holds 520 (unknown).
- **Things.**
  - +8 holds the absolute facing (1608 for the 90° placement), not one relative to the placement.
  - +118 holds the absolute height: floor + `Z`, 16 or −8 where OpenVirtue has 0.5 or 1.
  - +106 (3.0), +122, +126 and +146 (300) are unknown.
- **View state.** +0/+4, the view bits of +20 (`0x600`) and +22 (`0x80`) vary at run time, as expected.
- **Order.** Things and actors are not grouped. OpenVirtue's things come before its actors and
  interleaved with them, with runs of wall-sized records between. apathy's map lists all walls, then
  things, then actors, so the order is not the map's. The likeliest reading is the original's
  per-region object lists, one region after another; the synthetic levels had all objects in one
  region. The section is also 7900 bytes longer than 630 entity and 5894 wall records. Objects the
  engine creates for `ATTACH` (14 shadow sprites and 40 attached wall textures in apathy) would fit.

On the retail level the record "constants" are not constant: only 289 records carry 16383 at +4, 5 at
+26 and 1.0 at +102. These words vary, most likely with the texture, and still need mapping before a
retail level's save can be written.

## Round 13: a retail level loads (2026-10-30)
Three carriers (`events`, `unplaced`, and `base` again; save hashes `8839f28c`, `48622cb5`,
`d4846b18`) and a field-by-field comparison with the original's own apathy save (`c063e44c`) fill
in the records. A second patched copy of apathy saves and reloads its own save (`9724c341`) as the
baseline for load errors. The 5894 walls and 629 of the 630 things and actors are matched by
position.

- **Unplaced objects.** The 7900 extra bytes are the walls, things and actors the WDL declares but
  the map does not place: 46 in apathy (10 walls, 21 things, 15 actors, bullets and dropped pickups
  among them). They open the object section in reverse declaration order, with zero positions,
  region 0 and `INVISIBLE` set. The manual says this for things and actors (p.70); the save shows
  walls too. An unplaced wall's view bits are clear (+20 `0x42`, +22 `0x300`). An unplaced thing holds
  0 at +130, where every other record holds −1. The retail scripts use these objects as pools, for
  example `IF (bullet1.INVISIBLE == 1) { SET curBullet, bullet1; … }`.
- **Action numbers** follow the order in which each action's name first appears in the flattened
  script, as a declaration or as an earlier reference (`IF_NEAR do_pickup` before
  `ACTION do_pickup`). All 29 numbers read from the retail save fit this; none fits declaration order.
- **Event block.** A thing's or actor's record holds `IF_NEAR`, `IF_FAR`, `EACH_CYCLE`, `IF_HIT`,
  `EACH_TICK` and `IF_ARRIVED` actions at +146…+166, −1 for none. A wall's record has `ATTACH`'s texture
  number at +146, 0 at +150, then the same six slots at +154…+174; walls take no `IF_ARRIVED`. A thing's
  or actor's `ATTACH` is at +138. `IF_KLICK` does not compile on a thing or an actor ("Keyword unknown").
- **Walls.**
  - **Coordinates.** Vertices are cut toward zero to 1/1024 (8157 values fit only truncation, none
    only rounding). +118 and +130 hold the two vertices' heights.
  - **Length (+8)** is the square root of `dx²/1024 + dy²/1024`, each square cut to whole raw units
    first, then cut to 1/1024. 2050 reads 2049 and 245 reads 243; all 5591 compared walls fit.
  - **Regions.** +134 is the left region and +138 the right one.
  - **+4** is the texture's height as (pixels·1024 − 1) / `SCALE_Y` in a 16-bit word: 128 pixels at
    scale 2 reads −1.
  - **OFFSET_Y** starts one raw step above the map's or the WDL's value (−30 reads −30719/1024); a
    script's `SET` is kept exactly, and an unplaced wall gets no step.
  - **MAP_COLOR** is +92's low byte (−255 for 1, −256 for 0).
- **Things and actors.**
  - **Size (+0, +4)** is the sprite's width and height in the same form as a wall's +4 (Ammo's 120
    pixels at scale 48 read 2559).
  - **Position.** X/Y are cut toward zero. `ANGLE` runs from 0 to 2π and is cut (270° reads 4825).
  - **Other fields.** +48 is `ASPEED`, +126 the region, and +122 the floor's height under the object.
  - **Height (+118)** is 0 on the `GROUND`, the ceiling less the sprite's height for a `CANDELABER`,
    otherwise the floor.
- **The first section.**
  - **Player region.** +60 and +64 hold the player's region.
  - **Palette.** +48 and +56 hold 6 in apathy, which declares six palettes (`levelPal` last), and 1 in
    the probe level with its one palette. This is likely the palette in use.
  - **Constants.** +32 holds 1, +52 holds 1.0 and +68 holds −1.
  - **+36/+40** change between saves.
- **The header.**
  - **Script name.** The 13-byte script name field keeps the end of a 12-character name it held
    before, `…DL` and a NUL. It shows past a shorter name: `TITLE.WDL`, NUL, `DL`, NUL. apathy's save
    shows the same tail. The rest of that older name is never visible.
  - **Length word.** The word after the archive's name is the body's length plus 10000 in all seven
    saves compared (8763 → 18763, 1187701 → 1197701).
  - **Section 0, +36/+40.** With these written, the save-format probe's full saves match byte for byte
    up to these two words. +40 moves with the player (14.875 at `PLAYER_X` 5, 13.0 at 6), and +36 is
    0 or +40 less 1.0. It looks like view state; OpenVirtue leaves both 0, which loads.
- **`BMAP` in a `TEXTURE`.** The music room's textures list their bitmaps with `BMAP`, not `BMAPS`. The
  original takes it; the saved sprite sizes prove it.
- **Loading.** The patched level `LOAD`s OpenVirtue's capture of apathy, then runs its sampler slot for
  61 frames with the marker skill set. Section by section, splicing OpenVirtue's sections into the
  original's own save showed that only the first section caused the "2 Errors" the engine reported
  at shutdown. With the palette words written, OpenVirtue's whole save loads without errors.
  Section 4 and the per-region object order were still OpenVirtue's (200 bytes, reverse map
  order), so the original accepts both.
- **Section 4 is not the ways.** apathy's map has no `WAY` at all. The section opens with a count (124
  in apathy, 0 in the probe level) and holds screen-positioned records: x 50.0 and y 30, 60, 80…
  in steps of about 20, a layer-like 2, 1.0, and 2³¹−1. These look like the level's panels, texts and
  overlays. The original loads a save with none of them, but what that does to the screen after a
  load is not checked.

Also open: a script brings an unplaced object in by setting its X/Y and clearing `INVISIBLE`, but the
manual's `LOCATE` (p.90) says a beamed object's region is lost until `LOCATE`, and the retail drop
scripts never call it. Whether the original re-derives the region when `INVISIBLE` clears is not
probed; OpenVirtue keeps region 0 until `LOCATE`.

Left open: section 4's screen-element records, +36/+40 of the first section, +106 (the current frame's
`DELAY`, run-time state), the floor height on slopes (4 objects one raw step or more off), the
original's per-region object order, and whether a `FADE_PAL` changes the palette words.

## Open Questions
- Whether actors and things are one list in reverse map order or grouped by kind. The probe had
  one of each; OpenVirtue reads actors first, as the scheduler visits them. The retail save groups
  them by region, and the original loads OpenVirtue's reverse map order (round 13).
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
