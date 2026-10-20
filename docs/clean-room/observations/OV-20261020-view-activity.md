# OV-20261020-view-activity: what "in view" means for SKIP_FRAMES and BERKELEY walls
Date: 2026-10-20 (rounds 1–5)
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-border-kinds found that `SKIP_FRAMES` (p.113, default 5) moves unseen actors outside
their `DIST` only every 6th cycle, and OV-20261019-clip-detail that a `BERKELEY` wall runs its
`EACH_TICK` only while in view. Both left open what "in view" means:

1. Is an actor "not visible" when it is outside the `PLAYER_ARC` cone, when it is hidden behind a
   wall or a region step, or when it is not drawn at all (e.g. below the screen)? What does the
   `#VISIBLE` flag (p.70) report?
2. When does the 6-cycle count start, and how long is the skipped step?
3. Is a skipped `CAREFULLY` actor's long step collision-tested along the way?
4. Does a `BERKELEY` wall run its `EACH_TICK` while the player is within its `DIST` but looks away,
   and while it is in the cone but behind another wall?

## Why It Matters
OpenVirtue had no view test: every actor moved every cycle, `BERKELEY` walls always ran, and
`VISIBLE` only mirrored `INVISIBLE`. Retail guards patrol out of sight and retail levels hang
animated `BERKELEY` walls in corridors.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.53 (wall `#VISIBLE`, `SEEN`, `BERKELEY`),
  p.70–71 (thing/actor `#VISIBLE`, `BERKELEY`, `FAR`), p.111 (`PLAYER_ARC`), p.113
  (`SKIP_FRAMES`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261020-view-activity/` (round 1 at the top,
  `round2/` … `round5/`; `round3-failed-sampler/` holds the carriers of a generator bug, below).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`. Traces `OVVAX0.TXT`:
  view-cone `9235669a…`, view-occlude `c83636bc…`, view-pillar `7bbfd7a8…`, skip-phase
  `5b857dfa…`, skip-wall `95f2e25f…`, skip-pad `3e9133d9…`, berkeley-near `aadabe4e…`; round 2
  near-low `c4743419…`, long-wall `0e1dbfca…`, long-pad `f8dea516…`; round 3 range-d1 `5490a544…`,
  range-d2 `c5a6baa0…`, tilt-far `07d9f6c2…`, cone-edge `ca96a276…`, occlude-edge `2eb19ab5…`;
  round 4 range-shape `5e6d6d6b…`; round 5 range-fine-a and -b `af130627…` (identical).
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963, native Linux DOSBox-X 2026.10.01,
  `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. Runs went at 0.26 to 8 ticks per cycle, some changing
  midway; every reading below is in cycles, and the long-step cases set each actor's `SPEED` to
  its wanted step per cycle over `TIME_CORR` in every cycle so the steps do not depend on the rate.
- Fixture generator: `ovtool oracle prepare view-activity --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/ViewActivityProbeCases.cs`) on the trace room of the earlier probes
  (x −16…48, y −24…24, 16 high, the eye at 8, `PLAYER_ARC` 1). Actors are 2 × 2 steps. The global
  `EACH_TICK` sampler prints a `C` line in every cycle with each watched actor's X, Y and
  `VISIBLE` (read as `IF (syn.VISIBLE == 1)`).
- Cases:
  - `view-cone`: a slow way actor on (40, −16) ↔ (40, 16), `DIST 1`. The player at the origin faces
    +X for 24 cycles, −X for 36, then angle 0.6. `view-occlude`: the same with a plain wall from
    (30, −20) to (30, 0) hiding the way's south half. `view-pillar`: the pad island as a solid
    pillar (floor = ceiling = 16, plain walls), an actor behind it seen from (−8, −20), then from
    (−15, 21) where it is 8 away below the screen.
  - `skip-phase`: four way actors at x −14, 40, −10 (`FAR`) and −6 (`DIST 30`); the player faces
    +X (only the second in view) for 13, 7, 8 and 9 cycles, each followed by 30 cycles facing +Y
    (none in view).
  - `skip-wall`, `skip-pad`, and round 2 `long-wall`, `long-pad`: unseen `CAREFULLY` way actors
    walk at a free-standing `IMPASSABLE` wall at y = 12 or at the pad 2 steps up (plain walls). In
    round 1 the run was too slow for long steps; round 2 fixes the steps at 1.6, 0.8 and 3.2 a
    cycle (9.6, 4.8 and 19.2 per skipped move) from y 4, 8 and 2.
  - `berkeley-near`: a passable `BERKELEY` wall from (10, −4) to (10, 4) with `DIST 6` and a
    counting `EACH_TICK`, read through a wall synonym, and a plain wall from (5, −6) to (5, 6) in
    front of it. Stands of 8 cycles: O1 the origin facing +X; N1 (8, 3) facing +X; N2 (8, 3)
    facing −X; F1 (16, 0) facing −X; F2 (16, 0) facing +X; N3 (8, 3) facing +X.
  - Round 2 `near-low`: a way actor 6 ahead (below the screen with the eye at 8), the player facing
    it, facing away, then facing it with the eye lowered to 1.5.
  - Round 3 `range-d1`, `range-d2`: eight way actors behind the player at (20, 0) facing +X, at
    6, 8, … 20 away on y −1 ↔ 1, `DIST 1` and `DIST 2`. `tilt-far`: a way actor 30 ahead; the
    player faces it, then looks up (`PLAYER_TILT` −0.8, the actor below the screen), then faces
    away. `cone-edge`: a still actor 40 ahead, `PLAYER_ANGLE` from 0.40 to 0.62 in steps of 0.005
    a cycle. `occlude-edge`: the still actor behind the end of view-occlude's wall, `PLAYER_Y` from
    −5 to 5 in steps of 0.25 a cycle.
  - Round 4 `range-shape`: behind the player at (20, 0), actors on the axis 18.5, 19, 19.5 and 20
    away; off it at (6, −9) and (10, −14) (|dx| + |dy| 23 and 24, Euclidean 16.6 and 17.2); and at
    32 away with `DIST 40` and `DIST 1`. Round 5 `range-fine-a`/`-b`: eight actors on the axis
    19.6, 19.8, … 21 behind, run twice.

## Observations
| Case | Trace | Reading |
|------|-------|---------|
| `view-cone` | facing it: `VISIBLE` 1, moves every cycle. Turned away at cycle 25: `VISIBLE` 0 from 26, moves at 26, then only at 32, 38, … 62, by 2.39 (six times the 0.398 per cycle). At 56 the rate had dropped and the move was 0.148, six times that cycle's step, not the time since 50. Turned to 0.6 at 61: `VISIBLE` 1 from 62, every cycle from 63 | an unseen actor moves every 6th cycle by six times the cycle's step. The `VISIBLE` printed in a cycle and the moves printed one cycle later go together |
| `view-occlude` | `VISIBLE` 0 throughout, moves every 6th cycle | in the cone but behind a wall counts as not visible |
| `view-pillar` | behind the pillar: `VISIBLE` 0, every 6th cycle. At (−15, 21), 8 away below the screen: `VISIBLE` 0 but every cycle | a region step hides; the near actor is explained by the near range (range-*) |
| `skip-phase` | the never-seen actor moves at 4, 10, 16, …; the seen one, after each turn, six cycles after its own last move (21, 27, … then 58, 64, … then 96, 102, …): out of step with the first after the first and third turns, in step after the second. `FAR` and `DIST 30` actors move every cycle | the count starts at each actor's own last move |
| `skip-wall`, `skip-pad` | at 0.26 ticks a cycle the skipped steps were 0.3: the `CAREFULLY` actors stopped at y 10.951–10.955 | no long steps; same as normal steps |
| `long-wall` | steps of 9.6 and 4.8 cross the free-standing `IMPASSABLE` wall both ways (4 → 13.6, 10.4 → 15.2, 13.4 → 8.6); 19.2 from 2 lands at 21.2 | the wall in between is not tested |
| `long-pad` | 8.8 → 18.4 (inside the pad) stops at 10.951; 10.951 → 20.55 (beyond it) passes; 23 → 13.4 (inside) stops at 21.049; 21.049 → 11.45 passes; the 4.8 steps all end inside and stay at 10.95; 2 → 21.2 jumps it | a long step is tested at its end: one that ends inside a region the actor may not enter stops 1.05 short of the first wall on its way; one that ends beyond passes |
| `berkeley-near` | O1 (in the cone behind the plain wall, 14 away): `VISIBLE` 0, no runs. N1: `VISIBLE` 1, runs. N2 (within `DIST`, looking away): `VISIBLE` 0, runs in every cycle. F1 (10 away, seen from behind): `VISIBLE` 1, runs. F2: `VISIBLE` 0, stops after the turn cycle. N3: back one cycle after the beam | a `BERKELEY` wall runs while visible **or** within its `DIST`, as p.53 says; a wall hidden behind a wall is not visible; walls are seen from both sides |
| `near-low` | 6 ahead, below the screen: `VISIBLE` 0, every cycle; facing away: every cycle; eye at 1.5: `VISIBLE` 1 | near actors are never skipped (range-*) |
| `tilt-far` | 30 ahead: `VISIBLE` 1, every cycle. Looking up, the actor below the screen: `VISIBLE` 0, every 6th cycle. Facing away: every 6th | `SKIP_FRAMES` follows `VISIBLE`, the sprite actually on screen |
| `cone-edge` | `VISIBLE` 1 up to angle 0.517, 0 from 0.521 | the 2-wide actor 40 away leaves the cone with its centre at 0.500 and its last column at 0.525: it stays visible while part of it is on screen, losing it some 5 of its 29 pixels early |
| `occlude-edge` | `VISIBLE` 0 up to `PLAYER_Y` −3.0, 1 from −2.75 | it is visible as soon as its edge clears the wall's end (predicted −3), not its centre (0) |
| `range-d1`, `-d2` | 6 to 18 behind: every cycle; 20 behind: every 6th; the same for `DIST 1` and `DIST 2` | a fixed near range, not `DIST` |
| `range-shape` | on the axis 18.5–19.5: every cycle. Off it, \|dx\| + \|dy\| 23 and 24 (Euclidean 16.6, 17.2): every 6th. `DIST 40` 32 away: every cycle; `DIST 1`: every 6th. The actor 20.0–20.4 away moved every cycle | \|dx\| + \|dy\|, not Euclidean; `DIST` lifts skipping beyond the near range |
| `range-fine-a`, `-b` | 19.6 and 19.8: every cycle; 20.0 to 21: every 6th; both runs identical | the near range ends at \|dx\| + \|dy\| = 20, exclusive |

Round 2's first `cone-edge`/`occlude-edge` carriers and round 3's first `cone-edge` stopped before
`IF_START` (no `OVSTA0.TXT`, timeout): a generator bug wrote a skill name into the sampler action.
They were rerun after the fix as round 3's `cone-edge` and `occlude-edge`.

## Conclusion
Confirmed for patched V3.963:

1. **`VISIBLE` is set while some of the sprite is on the screen in the last drawn frame**: within
   the `PLAYER_ARC` cone (counting the sprite's width), not hidden behind a wall or a region step,
   and between the screen's top and bottom (an actor below the screen, or looking up past it, is
   not visible). A wall's `VISIBLE` works the same way, from either side.
2. **`SKIP_FRAMES` follows `VISIBLE`.** An actor that was not visible in the last drawn frame, is
   outside its `DIST`, has no `FAR` and is at least 20 from the player (|dx| + |dy|) moves only
   every (`SKIP_FRAMES` + 1)th cycle, counted from its own last move, by that many times the
   cycle's step. Nearer actors move every cycle whatever their `DIST` (not in the manual).
3. **A skipped `CAREFULLY` actor's long step is tested at its end only.** It passes when it ends in
   a region the actor may enter, with no wall it may not cross within its half width (plus the
   0.05 clearance) ahead; walls and raised regions in between are jumped. Otherwise it stops 1.05
   short of the first wall on its way, as a normal step does.
4. **A `BERKELEY` wall runs its `EACH_TICK` while it is visible in the last drawn frame or the
   player is within its `DIST`** (|dx| + |dy| to the nearer vertex, OV-20261019-wall-distance).
   Looking away within `DIST` keeps it running; a wall hidden behind another wall sleeps.

## Confidence
high for rules 2 (counting, step, `FAR`, `DIST`), 3 and 4: every stand and step agreed. high for
rule 1's cone, occlusion and below-the-screen parts; medium for its exact edges (one edge of the
cone and one wall end, each a few pixels early or on time). medium for the near range: five runs
put the edge at 20 (|dx| + |dy|, exclusive) and rule out Euclidean distance, but range-shape's
actor 20.0 to 20.4 away moved every cycle, which this rule does not explain.

## Open Questions
- Why range-shape's on-axis actor 20.0–20.4 away moved every cycle while range-fine's at 20.0
  skipped (it moved along the line of sight; the others across it).
- Whether `TRANSPARENT` walls hide what is behind them, and whether sprites hide each other.
- Whether a fast `CAREFULLY` actor that is not skipping is tested along its step or at its end.
- Whether a skipped long step is tested against things, other actors and the player at its end.
- Whether walls other than `BERKELEY` ones get a `VISIBLE` (and `SEEN`) flag the same way.

## Tests To Add Or Update
- `ViewTestTests`: the cone, a free-standing wall, a wall's end, a pillar region, the vertical
  window with tilt, a wall's back face and a wall behind another.
- `WdlViewActivityTests`: `Tick_SkipFrames_UnseenActorOutsideDistMovesEverySixthCycleBySixCycles`,
  `Tick_SkipFrames_CountStartsAtTheActorsLastMove`, `Tick_SkipFrames_FarAndDistLiftIt`,
  `Tick_SkipFrames_ActorsWithin20OfThePlayerMoveEveryCycle`,
  `Tick_SkipFrames_ZeroMovesUnseenActorsEveryCycle`,
  `Tick_SkipFrames_ActorHiddenBehindAWallInTheConeSkips`,
  `Tick_SkipFrames_LongCarefulStepIsTestedAtItsEndOnly`,
  `Tick_ObjectEachTick_BerkeleyWallRunsWhileInViewOrWithinDist`,
  `Tick_ObjectEachTick_BerkeleyWallFollowsTheLastDrawnFrame`.
- Actor tests whose mover starts behind the player set `SKIP_FRAMES` 0.

## Implementation Notes
ADR-0010: `OpenVirtue.Engine.Rendering.ViewTest` decides visibility headlessly from the player's
eye, `PLAYER_ANGLE` and `ProjectionModel` (cone, vertical window with the tilt shear), sampling a
sprite across its width and a wall along its part inside the cone, and walking the sight line past
walls (free-standing ones hide, region borders show only their opening; `INVISIBLE` and
`TRANSPARENT` walls are see-through; walls are indexed in a 16-step grid that follows moving
vertices). `WdlRuntime.RefreshView` runs it at the start of every cycle,
before the cycle's actions turn or move the player, so it is the last drawn frame. It sets
`VISIBLE` on things, actors and `BERKELEY` walls. `SkipFramesMultiplier` gates
`AdvanceActorTargets`; `TryEndPointCarefulMove` makes a skipped long step's end test;
`IsDormantBerkeleyWall` gates the object `EACH_TICK` loop. `SKIP_FRAMES` defaults to 5. The
original prints a cycle's moves one cycle after the `VISIBLE` they followed; OpenVirtue moves
actors before the global `EACH_TICK` slots, so in OpenVirtue they show in the same cycle.
