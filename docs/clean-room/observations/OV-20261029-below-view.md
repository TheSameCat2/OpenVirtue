# OV-20261029-below-view: what the eye sees of a BELOW stack
Date: 2026-10-29
Status: confirmed for views from outside the stack (one round) and implemented
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
Does the original's `#VISIBLE` flag (p.53, p.70) see into a stack's lower storey through the
opening beside it? Does the slab between two storeys hide what is above or below it?

## Why It Matters
`VISIBLE` drives `SKIP_FRAMES` and `BERKELEY` walls. OpenVirtue's view test knew only the map's
top regions, so it had two errors:
- It hid everything under a bridge: the wall's opening used the deck's floor height.
- It showed things through the slab to a viewer above them.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: the 1998 A3 manual p.53, p.59–61 (`BELOW`, `GROUND` placement),
  p.70 (`VISIBLE`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-below-view/`.
- Relevant local-only file hashes (sha256, first 8), `OVBLV0.TXT`: room-low `2c54d1bf`, room-high
  `420b1220`, deck `1e00349a`, away `78f27d94`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle prepare below-view` (`tools/OpenVirtue.Tools/BelowViewProbeCases.cs`),
  run with `SDL_VIDEODRIVER=dummy`.
- Bridge: the pad (x −12…−4, y 12…20) is a deck with floor 10 and ceiling 16. An underpass
  `bv_low` (floor 0, ceiling 8) is stacked under it, and visible passable walls bound it.
- Things:
  - `bv_lo` (`GROUND`, height 0) at (−8, 17) starts in the underpass (manual p.61).
  - `bv_hi` at (−6, 18) stands on the deck.
- Each case places the eye, waits 3 cycles, and prints both things' `VISIBLE` (1 or 0) and `Z`.

## Observations
| Case | Eye | `VISIBLE` underpass / deck | Reading |
|---|---|---|---|
| room-low | (−8, 2), height 4, facing the bridge | 1 / 0 | the underpass is seen through the opening under the deck; the deck's edge hides the deck thing |
| room-high | (−8, 2), height 15 | 0 / 1 | the slab hides the underpass; the deck thing shows over the edge |
| away | (−8, 2), height 4, facing away | 0 / 0 | control |
| deck | beamed to (−8, 13), feet at 10 | — | the original moved the player out of the stack to (−9.42, 9.46), so this is another room view (0 / 1) |

- Both things' `Z` read 0 and 10. The deck thing has no `GROUND` and no `HEIGHT`, yet its `Z`
  is the deck's floor height. OpenVirtue reads 0 (its offset above the floor).

## Invariant
A sight line from the eye passes each wall it crosses only through an opening between a storey
on the near side and a storey on the far side. Within a stack's footprint it stays in one storey,
so the solid between a storey's ceiling and the next storey's floor hides what lies beyond it.
Where the eye and the point share a footprint, they must share the storey too.

## Replay notes (2026-10-30)
OpenVirtue's replay now matches `away`, `room-low` and `room-high` in full. In `deck` it differs
only in the player's pushed-out Y (9.453 against 9.455). Three changes:
- **A thing's `Z`.** A thing's script `Z` is a skill of its own beside `HEIGHT`. It starts as the
  floor under the thing, so the deck's thing reads 10. OpenVirtue had made `Z` an alias of
  `HEIGHT` (OV-20261029-save-body rounds 6 and 13).
- **Seen from outside a stack.** A sight line that enters a stack from outside goes on only in the
  storey that holds the eye's height. From the room at eye 15 the underpass stays hidden, though a
  straight line reaches it under the slab; from eye 4 the deck's thing does.
- **A sprite in a storey.** It shows only between its storey's floor and ceiling. A sprite 16 high
  in the 8-high underpass does not reach through the slab.

## Open Questions
- Views from on the deck: the beamed player was moved out of the stack. Where the original puts a
  player beamed into a stack, and what it does with the feet above a lower storey's ceiling, is
  not measured.
- Reading `Z` of a thing without `GROUND`: the original gave the absolute height (10). OpenVirtue
  keeps `Z` as the offset above the floor. More cases (with `HEIGHT`, after `SET Z`) are needed
  before changing it.

## Implementation
`ViewTest` sorts the walls a sight line crosses and follows the storeys through them, with spans
of target heights per storey. `IsSpriteVisible` takes the sprite's storey. Unstacked levels give
the same results as before. Tests:
`BelowRegionTests.ViewTest_BelowStack_SeesThroughStoreysAndNotThroughSlabs`. `ovtool oracle diff
below-view` matches every `VISIBLE` value; it differs only in the open points above.
