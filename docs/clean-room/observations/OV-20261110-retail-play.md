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
  did in Apathy. The replay's clock used to split it into ordinary frames, which put every later line
  6.6 ticks off. Since round 4 it takes a sampler that printed every cycle as one: `oracle diff` now
  replays the long frame whole. apathy-idle and newage-idle then match on every line, and heart-idle on
  884 of 901. heart-idle and legalism-idle part at line 159, where the eye's countdown bob starts: the
  seconds clock's phase depends on the launch (OV-20261103-contact-clock), and these traces do not print
  `SECS`.
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
  the step over `TIME_CORR`, which turned −15 into −16. Only a step a wall or an object cut changes them
  (round 4: a wall keeps them too).
- **The engine's own step products round a half up, toward +∞**: −186 raw at `TIME_CORR` 0.25 stepped −46, not
  −47. Every earlier carrier agrees (none had a negative half).

After these the walk's x, y and velocities match the original through line 238 (y) and 317 (x), and
`PLAYER_VROT` on all 600 lines.

## Round 4: STEPS, WALK and the climb in the walk
Read from the same `apathy-walk` trace (`STEPS` raw, `WALK`, `PLAYER_Z`, the floor) and the `border-climb`
probe's new `edge-2` case (OV-20261019-border-climb, 2026-11-10 addendum). Apathy's `move.wdl` sets
`WALK_PERIOD` 8, `WALK_TIME` 6 and `PLAYER_SIZE = my_size + 0.3·WALK` (capped at `my_size` 5).

- **`STEPS` adds each move a cycle late, in 22.10: each component's square cut to 1/1024, their sum, the
  square root cut to 1/1024.** A move of (−11, 35)/1024 adds 32 (0 + 1 → √1·32), (−16, 51) adds 45 (0 + 2 →
  trunc(32·√2)), (−5, 15) and (−8, 24) add 0 (both squares under 1/1024), each in the cycle after the
  move. A player who creeps less than 32/1024 a component counts nothing. The save-format beam (5, 3) still
  gives trunc(1024·√34). All 239 lines before the step border match.
- **`WALK` is 1 − 2·|sin|, not a cosine**: with `a` the distance `STEPS` added since the last whole
  `WALK_PERIOD`, the angle is trunc(a·3216/period + 1/16) in 1/1024 radians (π cut to 3216/1024; of 234
  lines where it decides, fractions to 0.936 take the floor and from 0.938 the next step) and `WALK` is
  (1024 − 2·trunc(1024·sin))/1024: 1 at a period's start, −1 at its middle, a sharp top and a round bottom.
  The cycle in which `a` reaches the period keeps `WALK`, the next keeps it too and drops its distance, and
  `a` starts again from 0 (the three equal values at each top, 0.852, 0.938, 0.906, 0.906 …). A cycle that
  adds nothing keeps `WALK` (−0.078 for 25 cycles while the player turned in place). `WALK` reads 0 until
  the cycle after the one that first sees the player standing on its floor, then 1. The rule gives all
  600 lines to the printed digit.
  `WALK_TIME` played no part in this walk.
- **The climb test measures the feet from the eye this frame's `PLAYER_VZ` moves it to.** OpenVirtue moved the
  player across first and then the eye. With the eye rising 0.02 to 0.03 a frame, Apathy climbed from floor
  −6 to −4 with the feet 2.478, 2.479 and 2.484 below the new floor after the rise, and from −4 toward −2
  2.497. It was held at 2.502. Measured before the rise those were 2.509 to 2.527, which a strict
  `PLAYER_CLIMB` 2.5 would have blocked. Standing still, `edge-2` enters a step 1/1024 under the climb.
- **A wall stop keeps the script's velocity**: held 0.826 back at the −2 border, the player read `PLAYER_VY`
  0.902 on, walked up to it again and climbed it 9 cycles later with its eye higher. Sliding along a closed
  region's wall from line 399, it read the `ACCEL` velocities on every line. OpenVirtue wrote the cut step
  back (−3.3), and the player bounced. Only an object's push changes the velocity now. The cut step still
  counts toward `IMPACT_VX`/`IMPACT_VY`, which the manual calls the collision's change of speed. Round 3's
  "a wall cut changes them" came from a fault in OpenVirtue's own check: a step under 4/1024 made up to the
  minimum never counted as whole. That fault, not the walls, is why keeping the velocity on every stop
  seemed to cost four `contact-clock` carriers.
- **The turn steps by the engine's rounded product, `PLAYER_ANGLE += round(PLAYER_VROT·TIME_CORR)`** like the
  move. Cut toward zero, the angle fell 0.006 behind in 32 cycles at the same `PLAYER_VROT`.
- **`PLAYER_SIN` and `PLAYER_COS` are the sine and cosine of the 1/1024 angle, cut toward zero**: the start
  at 2412/1024 reads `PLAYER_COS` −0.706 (−723.7 → −723), and the action's force (0.1·1.3·`PLAYER_COS` and
  the same with the sine) then matches on all 600 lines. A 2048-, 4096- or 8192-entry sine table with
  rounded entries fits fewer lines. The saves' `PLAYER_COS` 1 at heading 0 holds as well.

After round 4 `apathy-walk` matches to line 398. From there its x and y are 1/1024 off while it slides
along the wall. Its angle, `PLAYER_VROT`, velocities and the action's force match on all 600 lines.

## Confidence
High for the head and the heights: `apathy-nosave` now replays the head's position on all 600 lines and
the player's x, y and angle exactly; `PLAYER_Z` is within 0.02 (an `ACCEL` rounding, open).

## Open Questions
- `PLAYER_Z` drifts up to 0.02 from the fifth frame on.
- apathy-walk from line 399: sliding along the closed region's wall from (−1.447, 42.397) to (−12, 32.5)
  with `PLAYER_WIDTH` 1.4, the original holds the player 1484.2 to 1485.9/1024 from the wall line, and
  OpenVirtue holds it 1483.4 to 1485.2. The original's slide per cycle is about the whole step projected
  onto the wall ((−18, −16) for a step of (−180, 156)), where OpenVirtue's comes out a unit short in one
  axis. No hold distance (1484 to 1485.5) or rounding (nearest, toward zero, away from the wall) fits the
  original's 72 cycles: its distance is likely taken in 22.10 from the vertices as stored. The position
  then drifts by up to 8/1024 and moves `STEPS`, `WALK` and `PLAYER_Z` from line 464.
- `PLAYER_COS` on the second line: the original keeps the start's −0.706 until the first frame, where
  OpenVirtue already shows the script's new angle.
- The idle runs' seconds clock: the replay would need the original's `SECS` to place the eye's countdown
  bob (heart-idle and legalism-idle from line 159).
- actor-width, settled for the first move: **on its first move since it was placed, an actor standing in its
  region within its hold of a wall is put at the hold and moves along the wall by the 4/1024 nudge only**
  (`w15-g125`: −10 to −9.996); later holds, and a walker coming in from rock (actor-step v-c-d), take the
  step's part along the wall. Implemented in `WdlRuntime.HoldCarefulActorAt`; w15-g125 and w15-g140 then
  match to line 15. Still open there: walking on beside the wall, the original holds the actor back to
  1.549 as soon as it drifts inside that, where OpenVirtue waits until it is within its 1.5.
- Scripted key input over a retail level (DOSBox-X types keys but cannot hold them).

## Implementation
`WdlRuntime.MoveActorTowardTarget` holds a careful actor off the walls of its recorded region;
`WdlRuntime.ArrivesAtBorders`; `Player.FeetPlaced`, set false by `RuntimeHost` for a loaded level and
used by `WdlPlayerMotion.Apply` and `Synchronize`; the host's pose snapshot keeps the published
`PLAYER_Z`; `WdlInterpreter.ExecuteAccel` and `WdlRuntime.EngineProduct` for round 3;
`WdlRuntime.CountPlayerSteps`, `WdlRuntime.AdvanceWalk` (with `PlayerPlaced` and `WalkPeriods`, which
`RuntimeHost` plays the floor's footstep from), `Player.ClimbEyeRise`, the velocity kept at a wall stop and the turn step in `WdlPlayerMotion.Apply`, and
`WdlPlayerMotion.SetHeading`, for round 4; `LevelLoader` and `PlayerStart.AngleRadians` cut the start to 1/1024. Tests: `WdlActorMotionTests.Tick_CarefulActorCrossingRegionBorder_DispatchesArrivalForMoveButNotForAVertex`;
`WdlRuntimeTests.Runtime_Steps_SquaresEachComponentIn1024thsAndCountsTheMoveACycleLate`,
`WdlRuntimeTests.Runtime_Walk_FollowsTheStepsThroughEachWalkPeriod`,
`PlayerTests.MoveHorizontal_IntoRegionBorder_MeasuresTheFeetFromTheEyeThisFrameRisesTo`,
`WdlPlayerMotionTests.Apply_HeldAtABorderItCannotClimb_KeepsTheVelocity` and
`WdlPlayerMotionTests.Apply_SolidWallCollision_KeepsVelocityAndAccumulatesImpact`.
