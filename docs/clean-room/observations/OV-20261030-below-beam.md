# OV-20261030-below-beam: where a player beamed into a BELOW stack lands
Date: 2026-10-30
Status: confirmed (one round) and implemented
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
OV-20261029-below-view left open where the original puts a player beamed into a `BELOW` stack
(p.59–61), and what it does when the eye is between a lower storey's ceiling and the storey above.

## Why It Matters
Saints of Virtue beams the player with `SET PLAYER_X/PLAYER_Y` and `LOCATE` (teleporters, level
starts), and its levels stack regions under bridges and water. OpenVirtue's `LOCATE` always chose
the stack's top storey. A player beamed into an underpass ended up on the deck, its eye lifted
through the slab.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: the 1998 A3 manual p.59–61 (`BELOW`), p.90 (`LOCATE`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261030-below-beam/`.
- Relevant local-only file hashes (sha256, first 8), `OVBBM0.TXT`: low-locate `05e8d6b9`, high-locate `578d361b`, mid-locate `7356502a`, low-nolocate `b13c62a5`, high-nolocate `b6e344d6`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle prepare below-beam` (`tools/OpenVirtue.Tools/BelowBeamProbeCases.cs`),
  run with `SDL_VIDEODRIVER=dummy`.
- below-view's bridge: the pad (x −12…−4, y 12…20) is a deck, floor 10 and ceiling 16, over an
  underpass, floor 0 and ceiling 8. The room, deck and underpass have `SKILL1` 1, 2 and 3.
- Each case beams the player to (−8, 16) with `PLAYER_SIZE` 4 and `PLAYER_Z` (the eye) 4, 9 or
  14, with or without `LOCATE`. It prints the position, `PLAYER_Z`, `PLAYER_HGT` and
  `HERE.SKILL1` for twelve cycles.

## Observations
| Case | Region | `PLAYER_Z`, `PLAYER_HGT` |
|---|---|---|
| eye 4, `LOCATE` | underpass (3) | 4, 0 throughout |
| eye 14, `LOCATE` | deck (2) | 14, 0 |
| eye 9, `LOCATE` | deck (2) | 9 at the beam; 10.35, −3.65 from the next cycle on |
| eye 4, no `LOCATE` | room (1) | 4, 0 |
| eye 14, no `LOCATE` | room (1) | 14, 10 (no fall in this probe, which has no movement script) |

## Invariant
- `LOCATE` puts the player in the stack's lowest storey whose ceiling is not below the eye, or
  the top storey when the eye is above them all. An eye between the underpass's ceiling (8) and
  the deck's floor (10) goes to the deck, where the eye clamp then lifts it to 0.35 above the floor
  (OV-20261019-eye-clamp).
- Without `LOCATE` a beam keeps the region (OV-20261019-proximity-events).

## Implementation
- `LevelTopology.StoreyHoldingEye` gives the storey.
- `WdlRuntime.Locate` uses it for the player.
- Tested by `BelowRegionTests.StoreyHoldingEye_PicksTheLowestStoreyWhoseCeilingIsNotBelowTheEye`.
- All five carriers replay line for line from the first sampled cycle. The beam lines before it
  differ only in the replay's start-up `TICKS`.

## Open Questions
- A thing or actor `LOCATE`d into a stack (only the player was probed).
- An eye exactly at a storey's ceiling.
