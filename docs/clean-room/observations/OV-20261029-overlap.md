# OV-20261029-overlap: a player who starts inside an actor
Date: 2026-10-29
Status: confirmed (three rounds, 21 carriers); implemented, and the walking contact too (2026-11-03 addendum)
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

## Addendum: the push-out in 22.10 (2026-11-03)
OV-20261103-startup-clock printed the player's raw position every cycle from `IF_START`. Its
`OVSUP0.TXT` traces: push-d21 `041d20fa`, push-d10 `41aeb573`, push-d01 `a6c3c6cb`, push-d005
`43819f47`, push-w4-14 `0b98cdd4`, last-far `f9a63e21`, wide16-3 `f0a5744a`, wide16-bb
`41046da6`, wide8-49 `3d729f9f`. It also re-reads this probe's round 3 `d15` (`OVOVL0.TXT`
`a80ba319`) and `d21` (`0c7bbff9`). It replaces the √(c² + 0.093) fit for the push-out and
settles three open questions:
- **The off-axis residual** comes from 22.10 arithmetic. Coordinates are cut toward zero, L² and
  C² = c²/1024 + 98 are cut, and the offset is scaled by √(C²/L²), with the ratio and its root cut.
  This gives `w8-14`'s (−4.661, 1.162), `still-off`'s (−1.759, 0.438), `d21`'s −0.119, `d15`'s
  −0.719 and `d01`'s 2.239. The 4-wide off-axis cases stay one unit short.
- **The 0.05 case** is pushed twice: to (−0.300, 0.004) in cycle 2, then out to (−2.172, 0.024) in
  cycle 3, along the line from that point. OpenVirtue still pushes once.
- **Which object.** A far actor placed last does not stop the push, so the first overlapping object
  counts, whether things or actors come last.

The push also reaches no object 8 steps or more away. That is why below-beam round 2 left the
player alone beside 16-wide objects 8.25 away.

Implemented in `LevelCollisionWorld.PushOutOfEntities`. The walking contact kept √(c² + 0.093) until
the still-walk addendum below.

## Addendum: walking into an actor, and the 0.05 start (2026-11-03)
Read from the existing traces, no new run: `still-walk` (`OVOVL0.TXT` `7ddf58c7`) and round 3
`d005` (the same layout as OV-20261103-startup-clock push-d005, `OVSUP0.TXT` `43819f47`).

**`still-walk`.** The start-up push leaves the player at (−1801, 449) raw beside the 2-wide actor at
(409, −102) (c = 1228 + 1024 = 2252). `PLAYER_VX` 0.01 is set once. Every cycle then adds exactly 4 to
x and 4 to y. About every eight cycles the player is put back about 0.02, and then creeps on toward
the actor. All 39 positions follow from three rules:
- **The step.** `PLAYER_VX` 0.01 times `TIME_CORR` is 3 raw or less. As for a `CAREFULLY` actor
  (OV-20261029-actor-step), a part under 4 becomes 4 with its sign, and a part of 0 becomes +4. That
  happens before the step meets walls and objects: the push-back below starts from the nudged point.
- **The test.** The step is taken in full, and then tested in 22.10: L² = (dx² + dy²)/1024, cut. At
  L² 4951 (cycle 10) the player is left alone; at 4946 and 4939 it is pushed. A threshold of 4947 or
  4948 fits all 39 lines, 4946 and below fits 18, 4949–4951 fit 26 and c²/1024 = 4952 fits 9. The
  rule taken is L² < (c − 1)²/1024 = 4948. One width only, so c² − 4 is not ruled out.
- **The push.** It is the start-up push-out of OV-20261103-startup-clock: the offset scaled to
  C² = c²/1024 + 98. From (−1761, 489), L² 4939, it gives (−1784, 495), the printed (−1.742, 0.483).
  The push does not reach `PLAYER_VX`: the player creeps on at +4 a cycle. Had it counted, as
  OpenVirtue's write-back of the speed moved did, the player would have walked away.

So walking contact with an object is no longer √(c² + 0.093) in floats. A straight walk into a 2-wide
thing now stops 2273/1024 from its centre, not 2.221.

**`d005`.** See OV-20261103-startup-clock, addendum of the same day: the push falls short when the
root's argument overflows, and finishes the cycle after.

Implementation:
- `LevelCollisionWorld.Move` takes `rawContact` (the player's). `ResolveEntities` then applies the
  test and `ScaleToContact`, and reports the push-back as `CircleMoveResult.ObjectPush`.
- `Player.LastObjectPush` is left out of the speed `WdlPlayerMotion.Apply` writes back.
- `WdlPlayerMotion.Apply` raises the step's parts to the 4/1024 minimum before the move, not after
  it. The minimum is not counted in the speed either.
- Test: `WdlRuntimeTests.Apply_PlayerCreepingIntoAnActor_StepsInUntilWithinTheContactThenIsPushedBackAndCreepsOn`.
  `PlayerTests.MoveHorizontal_ThingCollisionUsesTextureScaleAndLivePassableFlag` and
  `WdlRuntimeTests.Tick_PlayerProximity_PushingAgainstThingOrActorFiresNearEveryCycleOutsideDist`
  now expect the 22.10 stop.

Result, with the push-d005 fall-back: overlap matches 21 of 21 carriers (19 before),
proximity-events 3 of 5 (2), actor-contact 10 of 18 (9) and startup-clock 16 of 17 (15); all probes
415 of 640 (410). 332 more lines match, in 12 carriers (the actor-contact `dist-*` and `stop-*`
walks, clip-contact `contact-actor`/`contact-thing`, proximity-events `near-walk`), and no carrier
loses a line.
