# OV-20261103-contact-clock: the player's step into a thing, the seconds clock, a skipped follower
Date: 2026-11-03
Status: confirmed (ten carriers at 60 frames a second, two at 8 ticks a cycle) and implemented; the 3.2 step and a few one-unit pushes are open
Observer: OpenVirtue maintainers (black-box probe, trace values)
Implementer: OpenVirtue maintainers

## Question
Three things the earlier traces could not settle:
1. **The player's step into a thing.** How is the player put back after a step into a thing,
   for steps from 0.4 to 3.2? OV-20261029-overlap (still-walk) measured 4/1024 steps.
   OV-20261019-actor-contact stop-width (1.6 steps at 8 ticks a cycle) did not fit that rule
   (actor-contact 2026-11-03 addendum).
2. **The seconds clock.** What drives `SECS` and `EACH_SEC`? A second took 61 or 62 cycles
   and drifted from `TICKS` (OV-20261029-scheduler-order 2026-11-03 addendum).
3. **The skipped follower.** When and how far does a skipped `FOLLOW` actor first move after the
   start-up? actor-events follow-border kept a constant (0.031, 0.062) offset.

## Why It Matters
Every collision with a thing or actor goes through the first rule. Retail scripts time out with
`EACH_SEC`. The oracle replay needs both, and the start-up phase, to compare traces line for line.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `forcerate=60`, the SaintsX-patched DOS runtime
  V3.963 (`VRUN.EXE` sha256 `bba9f9bc`), `SDL_VIDEODRIVER=dummy`; trace files only.
- Reference material consulted: A3 manual p.111 (`PLAYER_VX`), p.117–118 (`TIME_CORR`, `TIME_FAC`,
  `TICKS`, `SECS`); no other.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261103-contact-clock/`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
`ovtool oracle prepare contact-clock` (`tools/OpenVirtue.Tools/ContactClockProbeCases.cs`). A global
`EACH_TICK` sampler, armed at the top of `IF_START`, prints `TICKS`, a cycle counter and `TIME_CORR`
every cycle, plus each case's values.
- **`step-` cases.** The player walks +X from (0, 0) with `PLAYER_VX` set to the step over
  `TIME_CORR` every cycle, toward a thing at (16, −20/1024), or on the line for `step16-on`. The
  thing is 2 wide (4 wide for `step16-w4`). The thing's `IF_NEAR` prints `MY.DISTANCE` and the
  position × 1024.
- **`clock-` cases.** `SECS`, `TOTAL_FRAMES` and an `EACH_SEC.1` line every cycle for 420 cycles.
  `clock-stats` adds `TOTAL_TIME`, `TIME_TOTAL`, `TIME_DRAW` and `TIME_FBUFFER`.
- **`follow-` cases.** actor-events follow-border's `CAREFULLY FOLLOW` actor on the pad (−8, 16),
  printed from cycle 1, in two launches.

All ten ran at 60 frames a second (`TIME_CORR` 0.25 and 0.28125). OVCCL0.TXT hashes (sha256, first 8)
are in `launches.md` beside the carriers.

## Observations
**The step into a thing.** The player is put back once per step, from where the whole step ends. The
4/1024 nudge is part of the step, and the push is the start-up push-out's 22.10 arithmetic to
C² = (c − 1)²/1024 + 102.
- The `step16-off` steps are 1638 raw. The fifth, from (13104, 32), ends at (14742, 36). L² from
  the thing is 2636, and the push puts the player at (14112, 57), as printed.
- The next four pushes, sliding round the thing, give (14126, 268), (14325, 950), (15482, 2067)
  and (17139, 2124), each exact.
- OpenVirtue split a step into sub-steps of half the player's width and pushed on the first one
  inside. That came out (14113, 55).

`step16-off` and `step08-off` match line for line. 35 of the 43 pushes in the step cases are exact.
The rest are off by 1 to 3 units:
- `step16-on` after its first push;
- two `step04-off` pushes;
- the 4-wide thing before the fix below.

**Things and actors.** The two give C² differently, and only from 4 wide does it show:
- a thing gives C² = c²/1024 + 98;
- an actor gives (c − 1)²/1024 + 102;
- for 2-wide objects both are 5050;
- for 4-wide things walked into (`step16-w4`; actor-contact `dist-4wide`, `dist-fine`) it is 10578,
  and for 4-wide start-up actors (OV-20261103-startup-clock) 10576.

Both are tested against (c − 1)²/1024. With this, `step16-w4` matches in full.

**A step longer than the contact.** `step32-off` steps 3276 raw. Its step ends 4 raw from the
centre (L² 1). The player is put at (14212, 648), 2272 from the centre, but not along any line the
other rules give. Open.

**The seconds clock.** It is real time:
- **The fit.** `SECS` ticks over when 0.991 × `TICKS` + c passes a multiple of 16. All 838 lines of
  `clock-sec` and `clock-stats` fit this.
- **Older traces.** anim-clock and order-wmp fit it as well.
- **The cause.** At 60 frames a second, `TICKS`, summed from 8/32- and 9/32-tick frames, runs 0.9%
  ahead of the seconds clock.
- **8-tick runs.** There the two keep step (each-sec-cadence, `SEC` at exact multiples of 16
  up to 176).
- **The offset.** c is how far the clock had run at the first cycle; launches give 1.03 to 1.35.
  In the replay, 1.28 puts every second of `clock-sec`, anim-clock, order and order-wmp where the
  original's are.

`TIME_TOTAL` is the frame's time in milliseconds: 16, 18 and 20 at `TIME_CORR` 0.25, 0.28125
and 0.3125, and 0 in cycle 1. `TIME_DRAW` and `TIME_FBUFFER` are the drawing's share (4 to 14 ms),
which depends on the host.

**The skipped follower.**
- **When it moves.** The `FOLLOW` actor, out of view, moves in cycles 6, 12, 18 and so on from
  the first cycle.
- **How far.** Each move is six times that cycle's own step: (0.25, −0.5) at `TIME_CORR` 0.25, and
  (0.281, −0.5625) at 0.28125.
- **follow-border.** Its offset of (0.031, 0.062) is one such move taken at 0.28125 instead of
  0.25. That is the start-up's 8/32 and 9/32 pattern in cycles its trace does not sample, not an
  engine rule.

## Invariant
- A player step meets things and actors once, at its end. The step includes the 4/1024 minimum
  parts. It is pushed back when L² < (c − 1)²/1024, to C² = (c − 1)²/1024 + 102, in 22.10.
- `SECS` and `EACH_SEC` follow the real-time second. `TICKS` sums the frames' quantised
  `TIME_CORR`, so the two drift apart by the quantisation (0.9% at 60 frames a second).
- `TIME_TOTAL` = 64 × `TIME_CORR` (milliseconds).
- A skipped actor moves in every sixth cycle from the first, by six of that cycle's steps.

## Confidence
- **High** for the single push at the step's end: exact over five pushes, and over two carriers in
  full.
- **High** for the seconds clock's form; its offset is launch-dependent.
- **High** for the follower's schedule.
- **Low** for steps longer than the contact distance.

## Open Questions
- `step32-off`'s first push, and the 1–3 unit residuals listed above.
- actor-contact `stop-width` with `PLAYER_WIDTH` 2 and 0.5: one line differs where c²/1024 + 98 and
  (c − 1)²/1024 + 102 part; how the player's width enters c is open.

## Round 2: at 8 ticks a cycle (2026-11-03)
`slow/step16-off` and `slow/step08-off`: the same carriers with `[cpu] cycles=fixed 1500`, which
ran at 8 ticks a cycle.
- **The first push.** From (13056, 32) the 1632 step and the nudge end at (14688, 36). L² is 2812,
  and the push gives (14113, 55), as printed.
- **The second push.** It gives (14125, 259), also as printed.

So the rule does not depend on the frame rate. actor-contact stop-width's first push (21.779, −13.928)
follows from it too: the "two steps ahead" lead of the actor-contact addendum came from a wrong
start position and is withdrawn.

## Results
Against the 2026-11-03 commit `9d3194b`, no carrier loses a line.
- **Contact.** `step16-off`, `step08-off` and `step16-w4` match in full. actor-contact `stop-width`
  and `stop-factor` gain 24 and 25 lines, `dist-4wide` 15, `dist-fine` 18, and clip-contact
  `contact-actor`/`contact-thing` 28 each.
- **Seconds clock.** `clock-sec`, scheduler-order anim-clock, order and order-wmp match in full.

## Tests To Add Or Update
- `PlayerTests.MoveHorizontal_StepIntoAThing_IsPushedBackOnceFromTheWholeStepsEnd`.
- `WdlRuntimeTests.Tick_SecondsClockRate_SlowsSecsAgainstTicks`.

## Implementation Notes
- `LevelCollisionWorld.Move` resolves things and actors for the player only at the end of the
  whole step, from the step's start, on the 1/1024 grid (rounded). This applies to steps no longer
  than the player's width (2 × `PLAYER_WIDTH`/2). Longer steps, unmeasured beyond `step32-off`
  (which did not pass through), keep the sub-step test, so a long move cannot pass through a thing.
- `LevelCollisionWorld.PushTargetSquared` takes c²/1024 + `ThingSlackSquaredRaw` (98) for a thing and
  (c − 1)²/1024 + `PushSlackSquaredRaw` (102) for an actor.
- `WdlRuntime.SecondsClockRate` and `SecondsClockStartTicks` default to 1 and 0. The oracle replay
  sets 0.991 and 1.28 for runs at the 60 Hz cadence, or a carrier's `"seconds_offset"`.
- `WdlRuntime` sets `TIME_TOTAL`.
