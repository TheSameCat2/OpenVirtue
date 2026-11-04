# OV-20261104 — The region and object instructions: SHIFT, ROTATE, LIFT, SHAKE, DROP, SET_ALL, PUSH, EXPLODE

Status: confirmed (eighteen carriers in three rounds, fast launches) and implemented; a handful of
details are open (below).

## Question
The retail scripts use eight instructions no earlier probe measured: `SET_ALL` (134 uses),
`SHIFT` (58), `SHAKE` (44), `DROP` (22), `ROTATE` (13), `EXPLODE` (13), `PUSH` (9) and `LIFT` (6).
The manual documents them (pp. 75, 90–93); OpenVirtue implemented them from the manual. What do
they do to the skills a script can read — positions, the walls' ends, `DISTANCE`, `HIT_DIST`,
`RESULT`, `HIT_MINDIST` — and in which cycle?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963; every run at about 0.26 ticks a cycle.
- Fixture generator: `ovtool oracle prepare instructions <dir>`
  (`tools/OpenVirtue.Tools/InstructionProbeCases.cs`) on the trace room (x −16…48, y −24…24,
  the eye at 8) with the pad island (x −12…−4, y 12…20, its wall named `in_pw`) as the region
  the region instructions act on; its `GENIUS` is an invisible actor at its centre (−8, 16).
  Things and actors placed with the WMP region field 2 belong to the pad. Every line is a token,
  `TICKS` and the case's skills; `C*`, `L` and `E` lines carry a cycle counter first.
- Traces (`OVINS0.TXT`, sha256 first 8): shift `16f1b20d`, rotate `fa0c63b1`, lift `377e579a`,
  set-all `bf722fb7`, push `c9ed4760`, explode `6c4cf9dd`; round 2 shift-shake `e8acc2ec`,
  shift-save `937a81c6`, shift-out `8c982a63`, set-all-near `d9e17ee4`, drop `8be4cbc7`,
  push-two and push-nohit `2d7e00fc`; round 3 shift-shake-wall `645de94d`, shift-shake-thing
  `9b74a017`, shake-thing `dc59b397`, push-low `1e83343a`, set-all-shake `868936e4`. (The round 1
  drop, set-all and explode carriers did not compile: `VISIBLE` and `INVISIBLE` are flags, read
  through `IF`, and a `PadWall` name needs its `WALL` declared.)
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Readings

### SHIFT, ROTATE, LIFT: once, then a SHAKE
| Carrier | Reading |
|---|---|
| shift | `SHIFT pad, 1.5, −0.25`: the thing at (−8, 16) and the standing actor at (−6, 18) of the pad's region moved to (−6.5, 15.75) and (−4.5, 17.75), the pad's wall ends with them, the player on the pad at (−10, 14) did not. Four more shifts a cycle apart from the action (0.3, 0.3), three from the pad's own `EACH_TICK` (0.1, 0) and one of (20, 0): nothing moved |
| shift-shake | the same with `SHAKE in_pw; SHAKE in_t; SHAKE in_a` after each shift: every shift of the action applied, 307/1024 a time (−6.2, −5.9, −5.601, −5.301); of the unshaken `EACH_TICK` shifts only the first (−5.201); the (20, 0) after them nothing |
| shift-shake-wall, shift-shake-thing | shaking the wall alone, or the thing alone, is enough |
| shift-save, shift-out | `FLAGS SAVE` on the pad, or the player left outside it: still only the first shift |
| rotate | `ROTATE pad, 0.5` about the `GENIUS` at (−8, 16): the thing at (−6, 16) to (−6.245, 16.958), the actor at (−8, 18) to (−8.958, 17.755), the wall's ends (−12, 12) and (−4, 12) to (−9.593, 10.571) and (−2.572, 14.406); the objects' `ANGLE` 0 throughout, the player at (−10, 16) unmoved, `PLAYER_ANGLE` 0; later rotations nothing |
| lift | `LIFT pad, 2`: the wall's `Z1`/`Z2` 2; `pad.FLOOR_HGT` 0, the thing's and actor's `Z` 0, the player's `PLAYER_Z` 8 and `FLOOR_HGT` 0 unchanged through twenty cycles; later lifts nothing |

- **A region instruction applies once; the region takes no other `SHIFT`, `ROTATE` or `LIFT`
  until one of its walls, things or actors is `SHAKE`n.** The manual's wheel example rotates
  from the region's `EACH_TICK` and sets `FLAGS SAVE`; neither matters here. The retail scripts
  shift regions by ±1000 and ±2 from actions that also move things (and shake them).
- **The region's vertices, things and actors move; the player on it does not.** Each part is a
  22.10 value (0.3 moves 307/1024). `LIFT` raises only the vertices (the walls' `Z1`/`Z2`):
  neither the region's `FLOOR_HGT`, nor its objects' `Z`, nor the floor under the player.
- **`ROTATE` turns positions, not `ANGLE`s**, with the sine and cosine of the 22.10 angle held
  in 16.16 and each product floored to 1/1024 before the sum (all four measured points, where
  cutting each product fits the objects but not the wall's ends).

### SHAKE
| Carrier | Reading |
|---|---|
| shake-thing | a thing of the pad's region with `EACH_TICK` printing `X`, `Y`, `Z`, its region's `SKILL1` and `DISTANCE`: `SET X, 4; SET Y, 2` (into the room) left `DISTANCE` at 24 and the region at the pad for three cycles; `SHAKE` made `DISTANCE` 4.472 (√(4² + 2²)) in that cycle and 6.000 (\|dx\| + \|dy\|) from the next; back to (−8, 16) with `SHAKE`: 17.888 then 24; `SET Z, 3` held, and the next `SHAKE` put `Z` back to 0 with 17.888 again; the region's `SKILL1` stayed the pad's 2 throughout |

- **`SHAKE` on a thing puts it on its floor and refreshes its `DISTANCE`** — the straight-line
  distance at once, the \|dx\| + \|dy\| frame from the next cycle on (the player stood still).
  A `SET` of `X`/`Y` alone refreshes nothing; `REGION` does not follow the position either way.

### DROP
| Carrier | Reading |
|---|---|
| drop | things with `DIST` 0, 6 (placed far away) and 4 (unplaced), the player at the origin: `SET PLAYER_ANGLE, 0.7; DROP …` put them at (0, 0), (6, 0) and (4, 0) — along the angle before the `SET`, 0; the unplaced one still `INVISIBLE`. `SET PLAYER_ANGLE, 2.5; SET MOUSE_ANGLE, 1; DROP …`: (4.588, 3.861) and (3.059, 2.574), along 0.7. After a beam to (−8, 16) and two cycles, `SET PLAYER_ANGLE, 4; DROP in_d6`: (−12.805, 19.586), along 2.5 from the beamed position, outside the pad, in the room (its region's `FLOOR_HGT` 0 where the pad's is 1); `Z` 0 throughout |

- **`DROP` goes `DIST` along the angle the cycle began with, from the player's settled
  position**, with the sine and cosine cut to 1/1024 (6 · 783/1024 = 4.588, 6 · 659/1024 =
  3.861; for 2.5, −820 and 612). The object keeps its `Z` and its `INVISIBLE` flag and takes the
  region its point falls in. `MOUSE_ANGLE` is not used without the pointer, and the runtime does
  not overwrite it while the player turns: it read 0, then the `SET`'s 1.

### SET_ALL and INVISIBLE
| Carrier | Reading |
|---|---|
| set-all, set-all-near | a thing placed three times, each printing `X`, `INVISIBLE`, `SKILL1`, `SKILL2` from its `EACH_TICK`, and another thing: `SET_ALL in_m.INVISIBLE, 1; SET_ALL in_m.SKILL2, 7`: no line from the three (an `INVISIBLE` thing runs no `EACH_TICK`); `SET_ALL …INVISIBLE, 0; SET in_m.SKILL1, 5`: all three back with `SKILL2` 7, `SKILL1` 5 on the first placed only (the last to run); `SET_ALL in_m.X, 12` (or 30): no line from the three ever again, `SET_ALL …INVISIBLE, 0` included; `SET_ALL in_o.SKILL2, 1` on the other thing: 1 |
| set-all-shake | after `SET_ALL in_m.X, 12`: `SHAKE in_m` and `SET in_m.X, 14` bring nothing back |

- **`SET_ALL` reaches every placement, `SET` the first placed.** `SET_ALL` on a position (the
  manual's "may not be changed") takes the objects out of the run for good.

### PUSH
| Carrier | Reading |
|---|---|
| push, push-two, push-nohit | from the origin facing +X with things ahead at 6 (`INVISIBLE`), 10, 16 and 30, left at 8, behind at 7, and a solid wall with `IF_HIT` at x 22: `PUSH` 5, 7, 9, 12 and 0.3 hit nothing (`HIT_DIST` and `HIT_MINDIST` read the push's distance, `RESULT` 0); 18 and 30 fired the thing at 16 (`HIT_DIST` 16, `RESULT` 0.111 and 0.467), never the one at 10, whichever was placed first and with or without an `IF_HIT` on the nearer; facing +Y or −X, set in the same action: the thing at 16 again |
| push-low | the same with the eye at 3: `PUSH` 12, 18 and 30 fired the thing at 10 (`RESULT` 0.167, 0.445, 0.667); facing +Y two cycles later, `PUSH 30` fired the thing at 8 to the left; back to +X, the one at 10 |

- **`PUSH n` fires the nearest object within `n` that the last frame drew**, whatever the player
  faces at the instruction: with the eye at 8 the 2-high thing 10 ahead is cut by the bottom of
  the screen and not drawn (OV-20261020-view-activity near-low), with the eye at 3 it is.
  `HIT_DIST` and `HIT_MINDIST` read its distance, `RESULT` 1 − the share cut to 1/1024
  (10/18 → 0.445, not 0.444); with nothing in reach both read `n`.

### EXPLODE
| Carrier | Reading |
|---|---|
| explode | an actor at (20, 0) with `FRAGILE` things 3, 6 and 12 from it, a plain one 4 away, the player at the origin: `SHOOT_RANGE` 10 fired the thing 6 away (`HIT_DIST` 4.001, `RESULT` 0.601) then the one 3 away (1.001, 0.900); 4 fired the near one alone (0.750); 20 all three from the far one (10.001, 0.5; 4.001, 0.801; 1.001, 0.950); from the pad, 36 from the actor, 30 the same with 0.667, 0.867, 0.967. After each, `HIT_DIST` and `RESULT` 0 and `HIT_MINDIST` 1.001; the plain thing and the actor itself never fired |

- **`EXPLODE` fires the region's `FRAGILE` objects from the last placed to the first, the source
  left out, each at the distance from the source's position to its nearest edge** (the centres'
  distance less the target's half width, 2 for these 4-wide sprites, a unit over), while within
  `SHOOT_RANGE`; `RESULT` 1 − the share cut to 1/1024; `HIT_MINDIST` the nearest. The player 20
  from the source with `SHOOT_RANGE` 20 was not hit.

### Round 4: walls under PUSH and SHOOT
| Carrier | Reading |
|---|---|
| push-wall (`a8738789`) | a solid wall with `IF_HIT` from (22, −4) to (22, 4), a thing behind it at 26 (not drawn) and one 8 to the left; from the origin facing +X: `PUSH` 25 nothing (`HIT_DIST` 25), `PUSH` 30 the wall with `HIT_DIST` 26.000 and `RESULT` 0.134 (1 − 26/30); facing +Y or −X two cycles later, nothing (the left thing, 2 high 8 away, is below the screen with the eye at 8); from x 10, `PUSH` 15 and 11 nothing |
| shoot-wall (`e258e55c`) | `SHOOT` at the wall from the origin: `HIT_DIST` 22, `RESULT` 0.956, `HIT_X` 15.999, `HIT_Y` 32.000 (the hit 4 along from the end at (22, 4), the eye at 8, 4 texels a step); from x 10: 12, 0.977, the same texels; facing 0.2: 12.227, `HIT_X` 6.398; facing −0.3: 12.570, 31.090; from y 2: 12, `HIT_X` 7.999; facing +Y from (10, 2): the room's north wall at 21.979, `HIT_X` 103.999 (26 along from its left end as seen); `SHOOT_RANGE` 10 from 12 away: nothing, `HIT_DIST` 0 and `RESULT` 0 |

- **`PUSH` takes a wall at its `DISTANCE`** (|dx| + |dy| to its nearer end, OV-20261019-wall-distance:
  26 for the wall 22 ahead reaching 4 aside, so `PUSH 25` misses it), and only while drawn.
- **A free-standing solid wall stops a `SHOOT`** (OpenVirtue went on to the room's wall behind it).
  A wall's `HIT_X` is the distance from its left end as seen, in texels, a unit under the
  product (15.999, 103.999); `HIT_Y` 32 with the eye at 8 on a 16-high wall of 4 texels a step.
- Open: the oblique shots. Facing 0.2 and −0.3 the hits read `HIT_DIST` 12.227 and 12.570 and
  `HIT_X` 6.398 and 31.090 where the ray through the 22.10 angle gives 12.242, 12.561, 6.308 and
  30.837: the hit lies as if the angle were 0.1974 and −0.3045. Facing +Y the north wall read
  21.979 for 22. Not modelled.

## Open
- `NEXT_THERE`: `THERE` is a region synonym in V3.963 — `SET THERE, in_m` on a thing and
  `SET in_th, THERE` on a `THING` synonym are type errors (66: `REGION` against `ACTOR/THING`) —
  and the retail loops walk regions of one name with `IF_EQUAL THERE, NULL` as the end. Whether
  `NEXT_THERE` wraps (manual p.89) or goes `NULL` after the last needs a room with several regions
  of one name, which the trace room cannot stage; unprobed.
- Which `SHAKE`s release a region: a wall or a thing of the region do; an object of another region,
  or the region itself, is untested.
- `LIFT`'s raised vertices never showed in the floor under the player; the 20 cycles measured may
  be short of a refresh, and a sloped region is untested.
- `SET_ALL` on a position: whether the objects are gone from drawing and contact too.
- `PUSH` on walls (the solid wall at 22 was never the nearest), `DROP` with the pointer active,
  `EXPLODE` on `FRAGILE` walls, and the edge distance of other widths.

## Implementation
`WdlRuntime`: `_unshakenRegions` (set by `ShiftRegion`, `RotateRegion`, `LiftRegion`; cleared by
`Shake` of a wall or an object of the region), `RotateMapPoint` (16.16 sine and cosine, floored
products), `Shake` (the floor and the distance), `DropObject` (`_cycleStartAngle`, 1/1024 sines,
the region of the point), `Push`, `Explode`, `FragileTargets` (reverse map order), `SetHitResult`
(the cut share), `RetireObjects` and `IsInactiveEntity`; `Wall` exposes `X1`…`Z2` from the
level's vertices; `WdlInterpreter.ExecuteSetAll` retires on a position; `RuntimeHost` leaves
`MOUSE_ANGLE` alone without the pointer. Round 4: `TryTraverseShotPortal` stops at a free-standing
wall unless `PASSABLE`, `CreateWallHitCandidate` keeps the texel's fraction a unit under the
product, and `Push` takes walls at their `DISTANCE` after `TestWallView`. Tests
`WdlRuntimeTests.Runtime_ShiftInstruction_MovesRegionVerticesAndObjectsOnceUntilAShake`,
`Runtime_RotateInstruction_RotatesRegionVerticesAndObjectsAroundGenius`,
`Runtime_LiftInstruction_MovesRegionVerticesOnly`,
`Runtime_DropInstruction_PlacesEntityAlongTheCyclesStartAngle`,
`Runtime_ExplodeInstruction_TriggersFragileTargetsAndReportsPlayerHit`. All eighteen carriers
replay line for line but for the start-up clock of the sparse ones; set-all, set-all-near,
set-all-shake, push, push-two and push-nohit match in full.
