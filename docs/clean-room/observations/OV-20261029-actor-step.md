# OV-20261029-actor-step: how an actor's ANGLE and SPEED become a step
Date: 2026-10-29
Status: confirmed (two rounds; round 1 failed to compile) and implemented
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
How does the original split a step along an actor's `ANGLE` into X and Y? The actor-events replays
left two open differences:
- OpenVirtue stepped by \|dx\| + \|dy\| and the original did not;
- the bullet drifted sideways in the original.

## Why It Matters
`MOVE` and `BULLET` actors, the retail projectiles and patrollers, follow this step every cycle.
A sub-unit difference grows over a level.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); trace files only.
- Reference material consulted: the 1998 A3 manual p.64–66 (`TARGET`, `SPEED`, `ANGLE`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-actor-step/` (round 2 in `round2/`).
- Relevant local-only file hashes (sha256, first 8), `OVAST0.TXT`: a0 `77ea99b7`, a-1 `691e655b`,
  a-quarter `438126aa`, a-2 `ac76fe73`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle check actor-step` (`tools/OpenVirtue.Tools/ActorStepProbeCases.cs`). The
  first round named a skill `as_a` like the actor (Error 135).
- Cases: an actor at (10, 0) with `TARGET BULLET` and `ANGLE` set to 0, 0.1, π/4, 1, π/2, 2, π or
  3π/2, at `SPEED` 0.5 (plus 1 and 0.25 at angle 0).
- Trace: its `EACH_TICK` prints X, Y, `ANGLE` and `TIME_CORR` times 1024 each cycle: the raw 22.10
  values.

## Observations
| Case | Raw steps (dx, dy) at `TIME_CORR` 256 / 288 | Fit |
|---|---|---|
| angle 0 | (128, 0) / (144, 0); speed 1: 256/288; speed 0.25: 64/72 | step = `SPEED·TIME_CORR` exactly; no drift |
| angle 804 (π/4) | (91, 90) / (102, 102) | 128·cos 0.78516 = 90.53 → 91, 128·sin = 90.49 → 90 |
| angle 1024 (1) | (69, 108) / (78, 121) | 69.16, 107.7; 77.8, 121.2 rounded |
| angle 2048 (2) | (−53, 116) / (−60, 131) | −53.27, 116.4; −59.9, 130.9 rounded |
| angle 102 (0.0996) | (127, 13) / (143, 14) | 143.3 → 143, 14.3 → 14 |
| angle 3216, 1608, 4825 | (−128, 0), (0, ±128) … | |

Every step of the ten cases fits. A bullet set to angle 0 does not drift. The 4-unit drift in
the actor-events bullet came with `CAREFULLY` and the WMP placement angle, not with this step.

### Round 3: `CAREFULLY`
Five more cases repeat angles 0, π/2, π, 3π/2 and 1 with `FLAGS CAREFULLY`.
- **Axis-aligned steps.** The step that has no X part (or no Y part) moves the actor 4/1024 along
  that axis, always positive. East and west steps gain +4 in Y, north and south steps +4 in X.
- **Diagonal steps.** At angle 1 the steps are those of round 2.
- **Wall stops.** Where a wall stops a `CAREFULLY` step, the 4 units still come (Y 232 → 236 at the
  stop), and the stopping coordinate is one unit nearer zero than OpenVirtue's rounded contact
  point.

This is the 4-unit drift of the actor-events bullet and of the border-climb and wall-distance
walkers. The border-climb player, moved by `PLAYER_VY` alone, creeps the same way: X goes from −8
to −7.992 in two steps. OpenVirtue applies the rule to the player too.

## Invariant
A step along an actor's `ANGLE` has the length `SPEED·TIME_CORR` in 22.10 fixed point. Its X part
is `round(step·cos θ)` raw units and its Y part `round(step·sin θ)`, where θ is the 22.10 `ANGLE`
and cos θ and sin θ are themselves cut toward zero to 22.10 (sin 90° is 1023/1024).
Steps toward a point are a different rule (\|dx\| + \|dy\|, OV-20261029-actor-events). A
`CAREFULLY` actor whose step has no X (or no Y) part is then moved 4/1024 along that axis, in the
positive direction, even when a wall stops it. A wall-stopped position is cut to 1/1024 toward zero.

## Addendum: the long step out of view (2026-10-29)
Bullets skipping frames out of view take six cycles' steps at once. Three of those long steps
could not come from exact cosines and sines:
- at angle 1 (1024 raw), 864 units gave (467, 726); no step length gives that with exact trig;
- at 0.785 (804 raw), 768 units gave (543, 542);
- at 90° (1608 raw), 768 units gave (0, 767).

All three are what cos and sin give when first cut toward zero to 22.10: 553 and 861, 724 and
723, 0 and 1023 raw. Over all 1977 recorded steps of rounds 2 and 3, this rule explains every step
the exact-trig rule explains, and these seven more. The rest are wall stops and nudges, which
neither rule covers.

## Round 4: steps toward a way point (2026-10-29, inconclusive)
Six carriers ran: a plain or `CAREFULLY` actor on a way from (10, 0) along +Y or toward (30, 7), at
`SPEED` 0.5, 2 and 6.4. The border-kinds actors (3-cycle samples at a very slow frame rate) showed
a `CAREFULLY` one 4–8 units off its way and losing 1–2 units a cycle against a plain one. No
rounding rule for the split by \|dx\| + \|dy\| fitted those samples.
- At `SPEED` 0.5 (`w-c-y-slow`) the `CAREFULLY` actor's X stays on a 4-unit grid and wobbles ±4
  per cycle, drifting about −1 a cycle. Its Y steps are 1–2 units short of the plain step
  (126/127 for 128, 142 for 144). It had already moved 188 units off the way before sampling began.
- The other carriers hit the room's walls or skipped frames out of view (`SKIP_FRAMES`), so they
  do not show single steps.

The probe needs `SKIP_FRAMES` 0 and a longer room before it can settle the rule.

## Round 5: steps toward a way point, settled (2026-10-29)
Five carriers ran. In each, the actor stands (`SPEED` 0) until the probe starts, `SKIP_FRAMES` is
set to 0, and then it gets `SPEED` 0.5 or 2. The ways run (10, −20) → (10, 20) and (−20, −20) →
(20, −5). Hashes (`OVAST0.TXT`, first 8): v-p-y `084faaf4`, v-c-y `ee5ded84`, v-p-d `870e40f0`.
- **The first cycle.** The first sample shows the actor still on the way's first point with
  `ANGLE` 0; it steps from the second cycle on. A `BULLET` steps in its first cycle. So a way
  actor given a speed spends a cycle arriving at the point it stands on. With `SPEED` 0 it does
  not arrive at all.
- **The split.** A step toward a point is split by \|dx\| + \|dy\| of the way left, each part cut
  toward zero to 1/1024. At 40:15, 128 units split 93 and 34, and 144 split 104 and 39.
- **The facing.** `ANGLE` is taken once per point, from where the actor sets off, cut toward zero
  to 22.10 (1608 along +Y, 367 at 40:15). It stays the same however far the actor drifts.
- **`CAREFULLY`.**
  - Along +Y the actor's X goes up 4 every cycle, and each Y step is one short (143 for 144). Four
    units off the line, the X part is 0 and the Y part is cut to 143. A part under 4 units becomes
    4 units with its sign (0 counts as +4).
  - At 232 units off the line the X part reaches −1 and the actor steps −4. From then on it wobbles
    ±4 about that line, as `w-c-y-slow` showed.
- **Residuals.** After about ten cycles some steps come out one unit shorter than the rule gives
  (143 for 144 on the +Y way). One `CAREFULLY` X turnaround comes a cycle early. A `TIME_CORR` held
  more finely than its printed 1/1024 would explain the short steps, but not the turnaround.
  The (−20, −20) carriers start outside the room, so they part ways at its wall.

Implementation: `WdlRuntime.AdvanceActorWay` (no arrival at `SPEED` 0),
`AdvanceActorToward` (raw-unit split, facing per point), `MoveActor` (the 4-unit minimum). The
round-5 traces now match for 13 to 84 lines before the first one-unit residual (3 before).

## Round 6: the one-unit residuals (2026-10-30)
No new carriers; the round 5 traces (v-p-y `084faaf4`, v-p-d `870e40f0`) and view-activity's walkers
were read again.
- **`TICKS` is held more finely than `TIME_CORR`.** It goes up 287, 288 or 289 raw per cycle while
  `TIME_CORR` reads 288. But the short steps do not follow those deltas.
- **The split rounds once.** A part of a step toward a point is the 22.10 product of the step and
  the distance left along that axis, rounded to 1/1024, then divided by \|dx\| + \|dy\| and cut
  toward zero. On the +Y way the X part is 0, and 144·39760/1024 = 5591.25 rounds to 5591, giving
  143.99 → 143. With 40032 left, 5629.5 rounds to 5630, giving 144.01 → 144. This fits all 87 steps
  of v-p-y and all 87 of v-p-d. The exact split, cut once, fits 55 and 85.
- **The step itself rounds.** `SPEED` 0.05 (51 raw) over six skipped cycles of 0.25 makes 76.5
  units, and view-pillar's walker stepped 77. So the engine rounds its own products to 1/1024,
  where a WDL `*` cuts toward zero (OV-20261029-numeric-edge). The angle step (round 2) uses the
  same product; no carrier tells the two apart there.
- **Result.** v-p-y and v-p-d now match in full (91 lines; 13 and 84 before), as do
  actor-events' vertex and way-start traces and view-activity's view-cone. Over all carriers,
  725 more lines match.

## Open Questions
- How a `CAREFULLY` actor slides after a wall stop, beyond the first stop.
- The `CAREFULLY` actors' residuals (v-c-y matches to line 77, v-c-y2 to 81, v-c-d to 35).

## Implementation
- `WdlRuntime.AdvanceActorForward` implements the rule; tested by
  `WdlActorMotionTests.Tick_Bullet_StepsByRoundedCosineAndSineOfItsAngle`.
- The `CAREFULLY` rules are in `WdlRuntime.MoveActor`.
- `ovtool oracle diff` replays 20 of the 25 carriers line for line. One differs from a `CAREFULLY`
  slide; the others differ in the printed `TIME_CORR` of the last cycles.
- The harness gained two things for this:
  - it puts printed `TICKS` back on the 1/1024 grid, preferring whole 1/32 ticks;
  - it takes the first sampled cycle's step from a printed `TIME_CORR` column.
