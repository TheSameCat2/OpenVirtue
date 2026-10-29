# OV-20261029-actor-events: how actors step toward targets and when IF_ARRIVED and IF_HIT run
Date: 2026-10-29
Status: confirmed (two rounds, six carriers); some details open
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
How far does an actor step per cycle toward a `WAY` point, a `VERTEX`, an object or the player, and
along its `ANGLE` (`MOVE`, `BULLET`)? When does it count as arrived, does it land on the point,
when does `WAYPOINT` advance relative to `IF_ARRIVED`, does a reached `VERTEX` keep triggering, and
where do the hooks run in the cycle?

## Why It Matters
OpenVirtue stepped `SPEED·TIME_CORR` along the straight line, arrived only on landing on the point,
ran `IF_ARRIVED` once per arrival, set the next waypoint as `TARGET_X/Y` before the hook, and moved
every actor before the global slot actions.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.64–70 (`TARGET` kinds, `WAY`, `WAYPOINT`,
  `TARGET_X/Y`, `IF_ARRIVED`, `IF_HIT`, `CAREFULLY`), p.18 (WMP `WAY`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-actor-events/` (round 1 at the top,
  `round2/`).
- Relevant local-only file hashes (sha256, first 8): patched `VRUN.EXE` `bba9f9bc`; `OVAEV0.TXT`:
  way `251db6e8`, thing-target `1f271eca`, follow-border `f667628f`, round2 vertex `8a176f9d`,
  move-border `1fc2ce93`, bullet `75c17977`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
Native Linux DOSBox-X 2026.10.01, `forcerate=60`, about 0.26 ticks per cycle; carriers from
`ovtool oracle prepare actor-events` (`tools/OpenVirtue.Tools/ActorEventsProbeCases.cs`). One actor
with `SPEED 0.5` and a 2-step-wide texture; its `EACH_TICK` prints `A` every cycle, `IF_ARRIVED`
prints `ARR`, `IF_HIT` prints `HIT`, each with `TICKS`, the global slot-1 count, `X`, `Y`,
`WAYPOINT`, `TARGET_X`, `TARGET_Y` (only while the window is open, after the scaffold's settling
`WAITT 16`).

Round 1 compile facts: an `ACTOR` definition accepts `TARGET NULL`, `FOLLOW`, `REPEL`, a way or an
object, but `TARGET MOVE`, `BULLET` and `VERTEX` are "Error(111): Missing/wrong parameter"; the retail
scripts set those with `SET x.TARGET, …` (23 `MOVE`, 10 `BULLET`, 7 `VERTEX`), as round 2 does.

## Observations
| Case | Trace | Reading |
|---|---|---|
| way (triangle (20,−6), (20,6), (10,6), from (10,−6)) | moving along x: 0.125 a cycle at `TIME_CORR` 0.25, 0.14 at 0.281; on the diagonal leg toward (10,−6)→(20,−6)… the (10,6)→(20,−6) leg: 0.088 and 0.099 straight-line | the step is `SPEED·TIME_CORR` in \|dx\| + \|dy\|: 0.5·0.25·√244/22 = 0.0887 |
| way arrivals | `ARR` at 0.098, 0.093, 0.025, 0.021 short (\|dx\|+\|dy\|) without landing; at 0.002/0.000 after a step that would have passed the point, which landed on it | arrived when less than about 0.1 is left, or on the point when the step reaches it |
| way, `ARR` line | `WAYPOINT` already the next one (3 → 1 after the last), `TARGET_X/Y` still the point reached; the next `A` line heads for the next point from where the actor stopped | |
| vertex and thing-target | not arrived at 0.138 and 0.134 left (\|dx\|+\|dy\|); next cycle on the point; then `ARR` in every cycle (73 lines) and the actor stays | a `VERTEX` or object target triggers `IF_ARRIVED` in every cycle it is there |
| order in a cycle | `ARR` before the actor's `A` line, with the same slot-1 count | the actor moves after the global slots, in its own turn of the object pass, before its `EACH_TICK` |
| follow-border, move-border | the actor (behind the player) moved every 6th cycle by 6 cycles' step (0.75 in \|dx\|+\|dy\| at 0.25) | `SKIP_FRAMES` (OV-20261020-view-activity); `FOLLOW` steps in \|dx\|+\|dy\| too |
| move-border | `ARR` 0.68 past the pad border at y = 12; the actor ends 1.05 from the wall at y = −24 | `IF_ARRIVED` at the border crossing |
| bullet (from (10,0) facing +x) | `HIT` in every cycle from 46.951 on (1.05 short of the wall at 48); x alternates 46.951/46.955 | `IF_HIT` in every cycle the bullet is blocked |
| headings | at angle 0 the bullet's y grows by 0.004 a cycle; the `MOVE` actor at angle 270 drifts in x by 0.004 a step; toward (20, 6) the y track lags OpenVirtue's by 0.01 after 26 cycles | the original's fixed-point angles |

## Invariant
- An actor heading for a point (`WAY`, `VERTEX`, an object, `FOLLOW`) or along its `ANGLE` (`MOVE`,
  `BULLET`) steps `SPEED·TIME_CORR` measured as \|dx\| + \|dy\| (times its `SKIP_FRAMES` cycles).
- If the step reaches the point it lands on it and arrives; otherwise it arrives once less than
  about 1/8 (\|dx\| + \|dy\|) is left, where it stands.
- On a `WAY` arrival `WAYPOINT` advances (wrapping to 1) before `IF_ARRIVED` runs; `TARGET_X/Y` show
  the next point only from the next step.
- A `VERTEX` or object target triggers `IF_ARRIVED` in every cycle the actor is there. A `FOLLOW`
  actor triggers it only at region borders (with `CAREFULLY`).
- Actors move in their own turn of the object pass (OV-20261029-scheduler-order), before their
  `EACH_TICK`.

## Open Questions
- Whether the arrival limit is a constant (OpenVirtue uses 1/8) or the cycle's step; the measured
  arrivals (≤ 0.098) and non-arrivals (≥ 0.134) do not separate them.
- The fixed-point heading drift (y +0.004 a cycle at angle 0).
- `FOLLOW`'s `IF_ARRIVED` at the pad border happened during the settling `WAITT 16`, outside the
  trace window; it was not observed.

## Implementation
`WdlRuntime.AdvanceActorToward` (the \|dx\| + \|dy\| step and `ArrivalDistance`),
`AdvanceActorForward`, `AdvanceActorWay`, `MoveActorTowardTarget` and `RunObjectPass` (the actor's
turn), with `DISTANCE` refreshed on read after a front actor moved. Tests: `WdlActorMotionTests`
(`Tick_AssignedWay_…`, `Tick_TargetVertex_StopsAtTargetAndDispatchesArrivalInEveryCycleThere`,
`Tick_NamedObjectTarget_…`).
