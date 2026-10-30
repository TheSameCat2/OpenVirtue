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
  `EACH_TICK.1` is word 139.
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
