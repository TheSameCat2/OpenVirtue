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
- **Waiting actions.** A `CALL`ed action waiting in `WAITT 200` made the body a different length.
- **Volatile bytes.** `base` and `base2` differ only in a few bytes of the skill section.
- **`AMBIENT` crash.** Writing `AMBIENT` on a thing or wall crashed the original at the
  instruction ("Engine crash at SET DO_ACTION"). On an actor it was accepted but not saved.
  `ASPEED` and `MAP_COLOR` are not object skills in A3 (Genesis "Error(50): Parameter unknown").

## Invariant
- A full `SAVE`'s fourth section is the object section: one 170-byte record per actor and thing,
  then one 178-byte record per wall, each kind in reverse map order. The fields are at the
  offsets above, as raw 22.10 values.
- Region state is not saved: a load keeps the level's region values.

## Open Questions
- Whether actors and things are one list in reverse map order or grouped by kind. The probe had
  one of each; OpenVirtue reads actors first, as the scheduler visits them.
- The rest of each record (texture, flags, region, way state), the 200-, 96- and 44-byte
  sections, the waiting-action layout, and whether `SAVE_ALL` regions are saved.
- Wall `SKILL5`–`SKILL8` (not probed).

## Implementation
- `AcknexSaveFile.ReadObjects` reads the object section.
- `RuntimeHost.ImportOriginalSave` copies each record onto the level's actors, things and walls
  when a full `LOAD` finds only an original save.
- Tests: `AcknexSaveFileTests.ReadObjects_ReadsTheFourthSectionsEntityAndWallRecords`, and the
  guarded `ReadObjects_SavesTheDosRuntimeWrote_HoldTheMovedThing`.
- Writing full saves the original can load needs the remaining sections.
