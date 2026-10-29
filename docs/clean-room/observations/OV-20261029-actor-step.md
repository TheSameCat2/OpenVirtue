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

## Invariant
A step along an actor's `ANGLE` has the length `SPEED·TIME_CORR` in 22.10 fixed point. Its X part
is `round(step·cos θ)` raw units and its Y part `round(step·sin θ)`, where θ is the 22.10 `ANGLE`.
Steps toward a point are a different rule (\|dx\| + \|dy\|, OV-20261029-actor-events).

## Open Questions
- When a bullet stops at a wall, its stopping position differs from OpenVirtue's by one raw unit
  (the original truncates).
- The sideways drift of the `CAREFULLY` bullet placed at WMP angle 0 in OV-20261029-actor-events.

## Implementation
- `WdlRuntime.AdvanceActorForward` implements the rule; tested by
  `WdlActorMotionTests.Tick_Bullet_StepsByRoundedCosineAndSineOfItsAngle`.
- `ovtool oracle diff` replays six of the ten carriers line for line. The other four differ only
  in the wall stop.
- The harness gained two things for this:
  - it puts printed `TICKS` back on the 1/1024 grid, preferring whole 1/32 ticks;
  - it takes the first sampled cycle's step from a printed `TIME_CORR` column.
