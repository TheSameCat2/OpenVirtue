# OV-20261019-wall-pass-clip: Wall IF_NEAR on passing or touching, and the CLIP_DIST gate on EACH_TICK
Date: 2026-10-19
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-wall-distance saw two wall behaviours it could not pin down:

1. Walking through a `PASSABLE` wall with `DIST 6` fired its `IF_NEAR` in the cycle of the
   crossing, while the player was outside `DIST`, and no `IF_FAR` followed. Does a crossing fire
   while the player is already within `DIST`? In both directions? Does a beam across the wall
   count? Does a crossing at or just beyond a vertex count? What does `DIST 0` do (p.51: "touching
   or passing through")? Does touching a solid wall fire it?
2. A wall's `EACH_TICK` ran only while its `DISTANCE` was below the player region's `CLIP_DIST`
   (largest value seen 9.982 with `CLIP_DIST 10`). Is the border `<` or `≤`? Are a thing's and an
   actor's `EACH_TICK` gated too, and by what: `CLIP_DIST`, or `THING_DIST`/`ACTOR_DIST` (p.105:
   the ratio of the thing's or actor's `CLIP_DIST` to the wall's)?

## Why It Matters
OpenVirtue fired a wall's `IF_NEAR`/`IF_FAR` only from `DIST` edges, so a `DIST 0` wall (the
manual's trigger wall) never fired, and ran every object's `EACH_TICK` at any range. The manual
says object actions only run within `CLIP_DIST` unless the object is `LIBER` (p.69, p.71).

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.30 (`CLIP_DIST`), p.51–53 (wall `DIST`,
  `#DISTANCE`, `IF_NEAR`, `IF_FAR`, `EACH_TICK`, `PASSABLE`), p.69–71 (object `#DISTANCE`,
  `EACH_TICK`, `LIBER`), p.105 (`THING_DIST`, `ACTOR_DIST`, `CLIPPING`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-wall-pass-clip/` (carriers, raw
  `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy of
  OV-20261019-wall-distance). Traces `OVWPC0.TXT`: pass-dist6 `566b3185…`, pass-dist0
  `e47e8ad1…`, solid-dist0 `68337dd4…`, solid-dist6 `9be93427…`, clip-wall `4d2b0144…`,
  clip-objects `9019d198…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. All six runs
  averaged 3.6–3.8 cycles per tick.
- Fixture generator: `ovtool oracle prepare wall-pass-clip --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/WallDistanceProbeCases.cs`, `PassClipCases`). It uses the room, the
  global `EACH_TICK.1` sampler and the walk/beam helpers of the proximity-events probe. The wall
  cases put one free-standing wall from v1 (24, −8) to v2 (24, 8) in the room, with the room on
  both sides. Its `EACH_TICK` counts its runs and prints the count, `MY.DISTANCE` and the player
  position at most every 2 ticks; `IF_NEAR`/`IF_FAR` print every run with their count,
  `MY.DISTANCE` and the player position. Walks along ±X drift slightly in +y (about 0.075 steps per
  step, as before); diagonal (+1, +1) walks do not. The printed positions are used throughout.
- Cases:
  - `pass-dist6`: the passable wall with `DIST 6`. P1/P2: walk through the centre (8 from both
    vertices) in +X from (20, 0), then back. P3/P4: walk along y ≈ 5 from (20, 5) to x ≈ 27.8 and
    back, entering `DIST` near v2 before the wall and crossing it within `DIST`. P5: beams from
    (20, 0) to (28, 0) and back.
  - `pass-dist0`: the passable wall with `DIST 0`. Q1/Q2: through the centre and back. Q3: creep
    at 0.05 steps per tick to x ≈ 23 (one step short), stand 32 ticks, creep across. Q4: beams
    across. V1–V5: diagonal walks that cross the wall's line x = 24 at y = 7, 7.5, 8 (v2 itself),
    8.5 and 9.5.
  - `solid-dist0`, `solid-dist6`: the wall without `PASSABLE`, `DIST` 0 or 6. S1: walk into its
    centre from (18, 0) and push for about 30 ticks, stand, back off. S2: walk at (+0.2, +0.1)
    steps per tick from (18, 0), sliding along the wall towards v2, stand, back off.
  - `clip-wall`: the passable wall with only an `EACH_TICK`, in a room with `CLIP_DIST 10`. The
    player is beamed to points at |dx| + |dy| 9, 10 and 11 from v2, along y = 8, diagonally
    (where the straight-line distance is 7.2–8.1) and along x = 24, and stands 32 ticks at each.
  - `clip-objects`: `CLIP_DIST 20`, a thing at (24, −12) and an actor at (24, 12) (no target,
    2-step texture), each with a counting `EACH_TICK`. Phase 1 prints `THING_DIST`/`ACTOR_DIST`
    and stands at |dx| + |dy| 19, 20, 21 (and 30) from each, straight and diagonally. Phase 2 sets
    `THING_DIST 0.5` and `ACTOR_DIST 0.25` and stands at 9, 10, 11 from the thing and 3–6 from the
    actor. Every stand is at least 24 from the other object.

## Observations
All runs 2026-10-19 17:16–17:19 UTC, six launches, one per case, all rc 0 with the completion
marker and no compiler error in `OVCON.TXT`.

| Case | Trace | Reading |
|------|-------|---------|
| `pass-dist6` P1/P2 | `N` at (24.005, 0.301), `MY.DISTANCE` 7.704, and on the way back at (23.955, 0.867), 7.178. No `F` | a walk through the wall fires `IF_NEAR` in both directions, outside `DIST`, with no `IF_FAR` |
| `pass-dist6` P3/P4 | P3: `N` at (20.938, 5.070), 5.992 (entering `DIST`); nothing at the crossing (x = 24, value ≈ 2.7); `F` at (27.600, 5.570), 6.029. P4: `N` at (27.550, 5.598), 5.952; nothing at the crossing; P4 ended within `DIST` and the next beam fired `F` (12.000) | a crossing while the player is within `DIST` fires nothing |
| `pass-dist6` P5 | beams (20, 0) → (28, 0) → (20, 0): no `N` | a beam across the wall is not a crossing |
| `pass-dist0` Q1/Q2 | `N` at (24.011, 0.301) and (23.961, 0.867). No `F` anywhere in the run | `DIST 0`: crossings fire `IF_NEAR`; `IF_FAR` never fires |
| `pass-dist0` Q3 | standing 1.05 steps short (22.954) for 32 ticks: nothing. Creeping across at about 0.013 steps per cycle: `N` at x 24.000 (cycle 953) and again at 24.014 (954), after 23.975 two cycles earlier | coming close to a passable wall does not fire. A step that ends on the wall's line counts, and so does the next step off it |
| `pass-dist0` Q4 | beams across: no `N` | as P5 |
| `pass-dist0` V1–V5 | `N` at (24.011, 7.011) value 1.000, (24.005, 7.505) 0.500, (24.011, 8.011) 0.021 — the last walk's line passes through v2 exactly. Crossings 0.5 and 1.5 beyond v2: nothing | the crossing test is the wall segment with its end points included, without any allowance for the player's width |
| `solid-dist0` S1 | the player stops 1.245–1.249 steps short of the wall. `N` in all 134 consecutive cycles of the push (`pe_n` 151–284), value 8.854 → 8.370; none while standing against it, none backing off. No `F` | pushing against a solid wall fires `IF_NEAR` in every cycle of contact |
| `solid-dist0` S2 | sliding along the wall towards v2: `N` in every cycle (`pe_n` 743–876), value 6.766 → 3.278 | sliding counts as contact |
| `solid-dist6` S1 | as solid-dist0: 134 consecutive `N`, value ≈ 8.8, no `F` | contact fires whatever `DIST`, while the player is outside it |
| `solid-dist6` S2 | `N` in every cycle from 743 until 774 (value 5.985, the first ≤ 6), then nothing for the 102 remaining cycles of the push. `F` when backing off at 6.046 | within `DIST`, contact fires nothing; the `DIST` edges work as before |
| `clip-wall` | `W` lines (15 per stand) only while standing at \|dx\| + \|dy\| 9: (15, 8), (19, 4), (24, 17). None at 10: (14, 8) twice, (18, 4), (24, 18); none at 11, including (17, 4), 8.06 in a straight line | a wall's `EACH_TICK` runs only while `DISTANCE < CLIP_DIST`, measured by axes; the border itself is outside |
| `clip-objects` phase 1 | `DEF`: `THING_DIST` 1, `ACTOR_DIST` 1. `T` only at 19 from the thing, `A` only at 19 from the actor; none at 20 (straight or (12, 8) diagonal), 21 or 30 | a thing's and an actor's `EACH_TICK` are gated the same way: `DISTANCE < CLIP_DIST` by axes, with both ratios at their default of 1 |
| `clip-objects` phase 2 | `THING_DIST` 0.5, `ACTOR_DIST` 0.25. `T` only at 9 (not 10, 11, or the (6, 4) diagonal); `A` at 3 and 4, not at 5 (straight or (3, 2) diagonal) or 6 | the thing's border is `CLIP_DIST × THING_DIST`, the actor's `CLIP_DIST × ACTOR_DIST` (5 here, not 0.25 × 0.5 × 20 = 2.5) |

## Conclusion
Confirmed for patched V3.963:

1. **A wall's `IF_NEAR` also fires when the player's movement passes through the wall or pushes
   against it, as long as the player is not within the wall's `DIST`.** It fires once per cycle
   of such movement, in either direction. A passable wall is passed when a step crosses the wall
   segment, end points included. A step onto the line and the next step off it both count. A
   solid wall fires in every cycle the player pushes against it or slides along it, but not while
   they stand still against it. A crossing or contact does not change the `DIST` state, so no
   `IF_FAR` follows it. Within `DIST` neither fires, and the `DIST` edges fire as in
   OV-20261019-proximity-events. With `DIST 0` the `DIST` test never fires. Then `IF_NEAR` comes
   only from passing and touching, and `IF_FAR` never fires.
2. **A beam across a wall is not a crossing.** Neither is coming close to a passable wall without
   crossing it.
3. **A wall's `EACH_TICK` runs only while its `DISTANCE` (|dx| + |dy| to the nearer vertex) is
   below the player's region's `CLIP_DIST`.** The border is excluded.
4. **A thing's `EACH_TICK` runs only while its `DISTANCE` is below `CLIP_DIST × THING_DIST`, and
   an actor's below `CLIP_DIST × ACTOR_DIST`**, by axes, border excluded. Both skills default to 1.

## Confidence
high for rules 1–4: every case behaved the same way at every point it measured, the borders
were probed from both sides and along two directions, and the readings agree with
OV-20261019-wall-distance (path A's pass-through `N`; path D's crossing at v1 within `DIST`, which
fired nothing). The on-the-line double `N` (rule 1) rests on one crossing, and positions are
printed to three decimals. What "on the line" means in the original's own number format is not
measured.

## Open Questions
Measured further by OV-20261019-clip-contact: region-border crossings, `LIBER`, the other gated
object actions, actor movement, thing and actor contact, and regions with different `CLIP_DIST`s.
No unenterable region border was found there, so contact with one is still open.

- Contact with a solid wall that separates two regions (a region border the player cannot step
  onto) was not probed; OpenVirtue treats it like a free-standing solid wall.
- Whether passing through a wall that separates two regions fires its `IF_NEAR` is not probed
  here; OpenVirtue treats every wall the same.
- The `LIBER` flag (p.71: the object runs its actions outside `CLIP_DIST`) and the other object
  actions the manual gates by `CLIP_DIST` (p.69, `IF_NEAR`/`IF_FAR` and so on) were not probed.
- Whether actor movement is also stopped outside `CLIP_DIST` (p.71, `LIBER`) is not measured.
- Thing and actor contact (the 307 `IF_NEAR`s of the flic-detail controls) is not covered here.
- Whether the gates use the region's `CLIP_DIST` when the player is in a region with its own
  value (only one region was used).

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_WallCrossingFiresNearOutsideDistOnly`: a walk through a
  passable `DIST 6` wall fires `IF_NEAR` in both directions with no `IF_FAR`, a crossing within
  `DIST` does not, and a beam across it does not.
- `WdlRuntimeTests.Tick_PlayerProximity_DistZeroWallFiresNearOnCrossingIncludingEndPoints`: with
  `DIST 0`, crossings through the segment and through a vertex fire, a crossing beyond the
  vertex and standing close do not, and `IF_FAR` never fires.
- `WdlRuntimeTests.Tick_PlayerProximity_PushingAgainstSolidWallFiresNearEveryCycleOutsideDist`:
  every cycle of pushing fires, standing still does not, and within `DIST` contact is silent.
- `WdlRuntimeTests.Tick_ObjectEachTick_WallRunsOnlyWithinClipDist`: the border is excluded.
- `WdlRuntimeTests.Tick_ObjectEachTick_ThingsAndActorsUseThingAndActorDistRatios`.
- `LevelCollisionWorldTests` for the crossing and contact report of a move.

## Implementation Notes
`LevelCollisionWorld.Move` can report the walls a move touched: those whose segment a step
crosses (end points included, three-sided so a step onto the line and a step off it both count)
and the solid walls that pushed the mover back. `Player.MoveHorizontal` collects them until
`WdlRuntime` takes them in the next cycle. A beam (`SET PLAYER_X/PLAYER_Y`, `Player.MoveTo`)
does not go through `Move`, so it reports nothing, as in rule 2. `DispatchProximityEvents` fires a
wall's `IF_NEAR` when the player entered `DIST`, `IF_FAR` when they left it, and otherwise
`IF_NEAR` when the wall was touched while the player is outside `DIST`. A `DIST 0` wall is tracked
for touches only.

The object `EACH_TICK` dispatch skips a wall whose `DISTANCE` is not below the player region's
`CLIP_DIST`, and a thing or actor whose `DISTANCE` is not below `CLIP_DIST × THING_DIST` or
`× ACTOR_DIST`. `THING_DIST` and `ACTOR_DIST` default to 1. Following the manual (p.71, not
measured), a thing or actor with `LIBER` set is not gated. Region `EACH_TICK` actions are not
gated.
