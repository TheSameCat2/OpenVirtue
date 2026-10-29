# OV-20261029-overlap: a player who starts inside an actor
Date: 2026-10-29
Status: confirmed (one round, five carriers); exact-centre rule implemented, push distance open
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
What happens to a player who starts the level inside an actor? The view-activity replays showed
three different outcomes:
- the player stayed on top of eight still actors in one layout;
- it was moved about 1.21 in two others;
- OpenVirtue pushed it 2.2 to 2.6 in all three.

## Why It Matters
Placements overlapping the start point occur in retail levels. The player's starting position
feeds every later trace.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: none beyond OV-20261019-actor-contact.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-overlap/`.
- Relevant local-only file hashes (sha256, first 8), `OVOVL0.TXT`: still-center `999d53d9`, still-off `d1f27a8e`, way-off `f0c2b07d`, still-walk `7ddf58c7`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle check overlap` (`tools/OpenVirtue.Tools/OverlapProbeCases.cs`).
- Actor: one 2-wide actor (contact distance 1 + `PLAYER_WIDTH` 1.2), `SPEED 0`. It is either
  still or on a way, either exactly on the player start (0, 0) or at (0.4, −0.1).
- Trace: the player's position every cycle from the probe start.

## Observations
| Case | Player at the first sample and after |
|---|---|
| still actor at (0, 0) | (0, 0) throughout |
| actor on a way at (0, 0) | (0, 0) throughout |
| still actor at (0.4, −0.1) | (−1.759, 0.438) throughout |
| actor on a way at (0.4, −0.1) | the same |
| … and `PLAYER_VX` 0.01 | (−1.759, 0.438), then walking on from there |

## Invariant
- **Exact centre.** A player exactly on an object's centre is not pushed: there is no direction out.
- **Off centre.** A player overlapping an object off its centre is pushed out during start-up,
  along the line between the centres. The original ends 2.225 from the centre for a 2.2 contact
  distance.

## Open Questions
- **The extra 0.025.** OV-20261019-actor-contact measured similar extras for wider objects (3.21–3.22
  at 3.2, 5.20 at 5.2). OpenVirtue pushes to exactly the contact distance.
- **Several overlapping actors at once** (the view-activity layouts): the original ended at
  (1.209, 0.119), OpenVirtue elsewhere.

## Implementation
`LevelCollisionWorld.ResolveEntities` no longer pushes a player out of an object whose centre it
sits on. The overlap replay now matches the two centre cases.
