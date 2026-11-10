# OV-20261110-retail-play: Retail Apathy replayed line for line
Date: 2026-11-10
Status: confirmed and implemented
Observer: Claude Code (Opus 5.5)
Implementer: same

## Question
Every earlier probe ran a synthetic level. Does a retail level, left to its own scripts, play out in
OpenVirtue as in the original, cycle for cycle?

## Why It Matters
Retail scripts combine instructions in ways no synthetic carrier did. A difference here is one a player
sees.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Reference material consulted: the 1998 manual p.65–66 (`MOVE`, `FOLLOW`, `VERTEX` and `IF_ARRIVED`),
  p.70 (`INVISIBLE` actors do not move).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261110-retail-play/` and `OV-20261110-actor-width/`.
  The carriers are the user's own Apathy files, loose, with `APATHY.WDL` renamed `TITLE.WDL`, `SET
  APPEND_MODE` commented out and a tracer action added to the start action. Nothing of them is committed.
- Relevant local-only file hashes (sha256, first 8), `OVSTR0.TXT`: apathy-idle `79f39cab`, apathy-nosave
  `39fe895b`, apathy-vert `c3933330`; `OVAWT0.TXT`: w1-g125 `d555cad9`, w15-g125 `2fc63692`, w2-g160 `5b9cdf1f`.
- Confirmation that no proprietary code/assets are committed: the note names scripts' effects and
  numbers the original printed, no script text.

## Method
A tracer prints, every cycle for 600 cycles, `TICKS`, the player's `X`, `Y`, `Z`, `ANGLE` and the first
placement's position of each enemy kind; `ovtool oracle diff` replays the same files through OpenVirtue.
`apathy-idle` is the level as shipped; `apathy-nosave` comments out its start's `SAVE`, whose disk write
made the original's second frame 8 ticks long, which no replay clock reproduces; `apathy-vert` prints
the vertical skills for 80 cycles. `actor-width` walks a careful actor 2 wide beside a wall at gaps from
0.8 to 2.1 with `ACTOR_WIDTH` 0.5 to 2.

## Readings
| What | Original | OpenVirtue before |
|---|---|---|
| the start action's beamed head (`SET X, Y`, then `TARGET VERTEX` to (−74, 93), `CAREFULLY`, its `IF_ARRIVED` hiding it) | walks straight there in 4.6 steps a tick and stands at (−74, 93), passing rock walls 4.0 to 4.6 from its path | held off a rock wall, the hold moved it into its true region, that fired `IF_ARRIVED`, the hook hid it and it stood at (−20.07, 33.79) for good |
| `PLAYER_Z`, `FLOOR_HGT`, `CEIL_HGT`, `PLAYER_HGT` in `IF_START` | 0, 0, 0, 0 | 1, −2, 20, −3 |
| `PLAYER_Z` after the first frame | 2.684 = −2 + 5 + 4·(−0.079): feet on the floor, then `PLAYER_VZ` | 2.074, then rising: the script's ground spring saw `PLAYER_HGT` −3 and launched the eye |
| actor-width | held at the half width × `ACTOR_WIDTH` + 0.049 (1.549, 2.049), untouched beyond it | the same |

## Rules
- **A script's beam leaves an actor's region as it was.** Its careful moves and holds go by that region's
  walls, so a head beamed far from its placed region meets none of the walls it passes.
- **A careful actor's `IF_ARRIVED` at a region border is for `MOVE`, `BULLET`, `FOLLOW` and `REPEL` only**
  (manual p.65–66). A `VERTEX`, way or object target arrives at its target.
- **The player's heights read 0 until the first frame, which stands the feet on the floor**: the eye then
  is the floor plus `PLAYER_SIZE`, before `PLAYER_VZ` moves it 4·`PLAYER_VZ` a frame. A `PLAYER_Z` the
  script set before then stands.
- `ACTOR_WIDTH` scales the distance a careful actor keeps from a wall, as OpenVirtue had it.

## Confidence
High for the head and the heights: `apathy-nosave` now replays the head's position on all 600 lines and
the player's x, y and angle exactly; `PLAYER_Z` is within 0.02 (an `ACCEL` rounding, open).

## Open Questions
- `PLAYER_Z` drifts up to 0.02 from the fifth frame on.
- `apathy-idle`'s 8-tick frame after the `SAVE`: the replay cannot know how long the original's disk write
  took, and a long first step changes where an actor walks.
- actor-width: in the cycle a careful actor is held off a wall, the original moves it along the wall by
  4/1024 only, where OpenVirtue takes the step's whole part along the wall (`w15-g125`: −9.996 against
  −9.500).
- Scripted key input over a retail level (DOSBox-X types keys but cannot hold them).

## Implementation
`WdlRuntime.MoveActorTowardTarget` holds a careful actor off the walls of its recorded region;
`WdlRuntime.ArrivesAtBorders`; `Player.FeetPlaced`, set false by `RuntimeHost` for a loaded level and
used by `WdlPlayerMotion.Apply` and `Synchronize`; the host's pose snapshot keeps the published
`PLAYER_Z`. Tests: `WdlActorMotionTests.Tick_CarefulActorCrossingRegionBorder_DispatchesArrivalForMoveButNotForAVertex`.
