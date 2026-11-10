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
- **The start position and heading are cut toward zero to 1/1024.**
- **The player's heights read 0 until the first frame, which stands the feet on the floor**: the eye then
  is the floor plus `PLAYER_SIZE`, before `PLAYER_VZ` moves it 4·`PLAYER_VZ` a frame. A `PLAYER_Z` the
  script set before then stands.
- `ACTOR_WIDTH` scales the distance a careful actor keeps from a wall, as OpenVirtue had it.

## Round 2: Heart, Legalism and New Age
The same tracer on the other three levels (`heart-idle` `2b0b019e`, `legalism-idle` `ac0fb390`, `newage-idle` `ad1f75f0`,
900 cycles each, the start's `SAVE` commented out), printing the player and the first placement of each
level's four commonest actor kinds:

- Every actor column matches on all 900 lines of all three.
- **The start's position and heading are cut toward zero to 1/1024**: Heart's start y 168.031 reads 168.030
  and its 135° reads 2.355 (2412/1024); OpenVirtue printed 168.031 and 2.356.
- A frame after the start's `WAIT 4` takes 6.6 to 6.8 ticks in the original (a disk load), as the `SAVE`
  did in Apathy; the replay's clock cannot know it, so the lines run 6.6 ticks apart after it.
- Matched by `TICKS` instead of by line, the player's eye starts its countdown bob at 47.97, 48.25 and
  48.16 in the original and 47.47, 47.44 and 48.16 in OpenVirtue: the seconds clock, which runs on real time
  (OV-20261103-contact-clock), within a tick, and New Age's to the line.

## Round 3: the player walked by its own script
`apathy-walk` (`OVSTR0.TXT` `99f4c9a5`) drives Apathy's own movement action from the tracer, `FORCE_AHEAD` 1 for 200
cycles, `FORCE_ROT` 0.5 for 60, then both for 200, and prints the velocities and the action's force raw
(× 1024), which a fixed-point rule needs.

- **`ACCEL` is the engine's 22.10 arithmetic, every product cut toward zero: `v′ = trunc(v·(1 − trunc(t·F/I)))
  + trunc(t·a/I)`** for each tick `t` of the cycle. With the action's force −63/1024 and `FRICTION` 0.2 it
  fits 59 cycles exactly (0, −15, −31, −44, −56 …). OpenVirtue's doubles stepped −16, −32, −48.
- **A step taken whole leaves `PLAYER_VX`/`PLAYER_VY` as the script set them.** OpenVirtue wrote them back as
  the step over `TIME_CORR`, which turned −15 into −16. Only a step a wall or an object cut changes them.
- **The engine's own step products round a half up, toward +∞**: −186 raw at `TIME_CORR` 0.25 stepped −46, not
  −47. Every earlier carrier agrees (none had a negative half).

After these the walk's x, y and velocities match the original through line 238 (y) and 317 (x), and
`PLAYER_VROT` on all 600 lines.

## Confidence
High for the head and the heights: `apathy-nosave` now replays the head's position on all 600 lines and
the player's x, y and angle exactly; `PLAYER_Z` is within 0.02 (an `ACCEL` rounding, open).

## Open Questions
- `PLAYER_Z` drifts up to 0.02 from the fifth frame on.
- `STEPS`, which drives the walking bob through `WALK`: the original's counts nothing in a walk's first three
  cycles and then about 0.003 to 0.013 less than the distance moved each cycle, a cycle late; OpenVirtue
  counts the distance in the cycle it is moved. No simple rule fitted, and the eye's bob drifts from it.
- apathy-walk from line 241: the player walks up to a 2-step border (floor −4 to −2) with `PLAYER_CLIMB` 2.5.
  The original takes the step to 0.861 from it and pushes the player back to `PLAYER_WIDTH` + 0.049 the next
  cycle, keeping `PLAYER_VY`; OpenVirtue refuses the step itself and writes the velocity back, so the player
  bounces. The climb test compares the floor with the feet, `PLAYER_Z − PLAYER_SIZE`, and Apathy sets
  `PLAYER_SIZE` to its size plus 0.3·`WALK`, so the bob decides a step this close to the limit: `STEPS` (and
  so `WALK`) is the cause to settle first. Keeping the velocity on every wall stop loses four contact-clock
  carriers, so the original does write it back after a solid wall.
- `STEPS` raw, from the walk's start: the original adds 0, 0, 0, then 32, 45, 45, 71, 71, 78, 101, 96, 106
  while the player moves 15.8, 25.3, 36.7, 53.5, 57.6, 76.5, 76.5, 85.1, 105, 101.8, 108.2, 130 (all 1/1024);
  neither the distance, the speed nor either times `TIME_CORR`, a cycle or two late, fits the pairs.
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
`PLAYER_Z`; `WdlInterpreter.ExecuteAccel` and `WdlRuntime.EngineProduct` for round 3; `LevelLoader` and `PlayerStart.AngleRadians` cut the start to 1/1024. Tests: `WdlActorMotionTests.Tick_CarefulActorCrossingRegionBorder_DispatchesArrivalForMoveButNotForAVertex`.
