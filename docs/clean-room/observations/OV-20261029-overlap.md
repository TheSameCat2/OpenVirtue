# OV-20261029-overlap: a player who starts inside an actor
Date: 2026-10-29
Status: confirmed (three rounds, 21 carriers); implemented. A residual of up to 0.008 off the axes is open
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
- Relevant local-only file hashes (sha256, first 8), `OVOVL0.TXT`: still-center `999d53d9`, still-off `d1f27a8e`, way-off `f0c2b07d`, still-walk `7ddf58c7`; round 2 w1-axis `796d6efd`, w8-axis `ac564119`, w4-14 `34afcb5f`; round 3 d01 `12eb365b`, d21 `0c7bbff9`.
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

### Round 2: width and direction
One still actor of width 1, 2, 4 or 8 (contact distance c = half the width + 1.2), placed so the
player start lies on an axis, on a diagonal or at 14° from it.

| Actor | Player after | Distance from the centre | c | √(c² + 0.093) |
|---|---|---|---|---|
| 1 wide at (0.3, 0) | (−1.428, 0.004) | 1.728 | 1.7 | 1.727 |
| 2 wide at (0.3, 0) | (−1.921, 0.004) | 2.221 | 2.2 | 2.221 |
| 4 wide at (0.3, 0) | (−2.915, 0.004) | 3.215 | 3.2 | 3.215 |
| 8 wide at (0.3, 0) | (−4.909, 0.004) | 5.209 | 5.2 | 5.209 |
| 2 wide at (0.3, −0.3) | (−1.271, 1.271) | 2.222 | 2.2 | 2.221 |
| 4 wide at (0.3, −0.3) | (−1.973, 1.973) | 3.215 | 3.2 | 3.215 |
| 4 wide at (0.4, −0.1) | (−2.724, 0.679) | 3.220 | 3.2 | 3.215 |
| 8 wide at (0.4, −0.1) | (−4.661, 1.162) | 5.216 | 5.2 | 5.209 |

### Round 3: start distance
A 2-wide actor on the x axis 0.05, 0.1, 0.6, 1, 1.5 and 2.1 from the player, plus at (0, 0.3) and (−0.3, 0).
- The player ends 2.219–2.222 from the centre in every case except 0.1, which gives 2.239.
- The y axis case gives (0.004, −1.921) and the −x case gives (1.921, 0.004).
- At 0.05 the y part is 0.024, not 0.004.

### The view-activity layouts again
- Eight 2-wide actors at x = 0.4 … −1 on y = −0.1: the player ended at (1.209, 0.119). That is
  2.22 from the *last placed* actor at (−1, −0.1), on the line through the start, while it still
  overlaps six others.
- The same on y = 0: it ended at (1.219, 0.006).
- Actors at x = 14 … 0 on y = −1: it ended at (0.004, 1.221), out of the last one at (0, −1).
- Actors at (1.5, 0) … (0, 0) and further ones: it stayed at (0, 0). The fourth actor sits exactly
  on the start. It is the first overlapping one counting from the last placed, and nothing pushes.

## Invariant
- **Exact centre.** A player exactly on an object's centre is not pushed: there is no direction out.
- **Off centre.** A player overlapping an object off its centre is pushed out during start-up,
  along the line between the centres.
- **One object only.** Only the first overlapping object counts, taking the objects from the last
  placed backwards. That is the reverse map order the save file and the scheduler use
  (OV-20261029-save-body). If that object's centre is exactly under the player, there is no push.
  The player is not pushed out of the others it still overlaps, then or in later cycles while it
  stands still.
- **Contact distance.** The push ends at √(c² + 0.093) from the centre, where c is half the
  object's width plus `PLAYER_WIDTH`. That is about 0.03 beyond c at c = 1.7 and 0.01 beyond at 5.2.
  The same fit holds for OV-20261019-actor-contact's earlier measurements:
  - stop distances 2.22 / 3.21 / 5.20 at `PLAYER_WIDTH` 1.2;
  - 3.01 / 4.01 / 6.01 at 2;
  - 1.53 / 2.52 / 4.51 at 0.5;
  - an actor pushing the player 3.212–3.221 ahead at c = 3.2.

  So this is the player's contact distance with objects in general. It is fitted, not derived:
  0.093 ± 0.003.
- **Nudge.** A push with no y part (or no x part) moves the player 4/1024 along that axis as
  well, always positive, like a player step (OV-20261029-actor-step).

## Open Questions
- **Off the axes and diagonals** the original lands up to 0.008 further out along x (at 14°:
  −2.724 vs −2.719, −4.661 vs −4.653). That is as if the direction were (0.9717, 0.2423), slightly
  longer than 1 and at 14.00° rather than 14.04°.
- **Odd cases.** The 0.1 start distance ends at 2.239. At 0.05 the nudge is 0.024, and in the
  y = 0 view-activity layout it is 0.006.
- **Order of things and actors** in the one-object search: unmeasured. OpenVirtue takes actors
  before things, as in the save.
- **Actors against things.** Whether their contact uses the same √(c² + 0.093) is unmeasured;
  OpenVirtue keeps c.

## Implementation
- `LevelCollisionWorld.ResolveEntities` no longer pushes a player out of an object whose centre it
  sits on.
- `LevelCollisionWorld.PushOutOfEntities` pushes out of the first overlapping object in reverse map
  order only, and applies the nudge.
- `Player.PushOutOfObjects` runs only when the position was set from outside since the player's
  last movement (start-up, a beam).
- `LevelCollisionWorld.ContactDistance` (√(c² + `ContactSlackSquared`)) gives the player's contact
  distance in its collision moves, its pushes and actor pushes.
- Results:
  - the overlap probe matches 12 of 21 carriers, up from 2; the rest are off by 0.001–0.008;
  - the view-activity start positions are within 0.002 of the original's;
  - all probes match 169 of 431, up from 159.
