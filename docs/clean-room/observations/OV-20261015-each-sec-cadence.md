# OV-20261015-each-sec-cadence: When do the global EACH_SEC slots run?
Date: 2026-10-15
Status: confirmed (runs 2026-10-15; implemented the same day, issue #8)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
The retail scripts assign actions to `EACH_SEC.n` with `SET EACH_SEC.n, action;` and clear them
with `SET EACH_SEC.n, NULL;`, as they do for the documented `EACH_TICK.n`. The 1998 A3 reference
manual never mentions `EACH_SEC`, and neither does the 2001 A4 manual. For a global `EACH_SEC`
slot:

1. Is `SET EACH_SEC.n` accepted by the patched V3.963 compiler, and does the slot's action run
   once per second (16 ticks), every frame, or never?
2. **Phase.** Does the first run come one second after the `SET`, or at a second boundary the
   runtime keeps for all slots?
3. **Order.** In a frame where both run, does `EACH_SEC` run before or after the global
   `EACH_TICK` slots?
4. **Slot count.** Are there 16 slots like `EACH_TICK`? Is slot 17 a compile error?
5. **`WAIT` inside.** If the action waits longer than a second, does each second start another
   instance, is the slot skipped while one waits, or is `WAIT` refused?
6. **Flic.** Does `EACH_SEC` keep running during `PLAY_FLICFILE`, as the 16 global `EACH_TICK`
   actions do (p.82)? Not probed here, see Open Questions.

"Wall-clock second" and "16 ticks" are the same quantity in OpenVirtue (`TICKS` and `SECS` are
both sums of the same capped frame time), and DOSBox-X at `forcerate=60` keeps them equal in the
original as well, so this probe does not try to separate them.

## Why It Matters
GitHub issue #8. OpenVirtue ignored `SET EACH_SEC.n`, so every retail timeout built on it never
fired. A local survey of the six retail worlds (`ovtool wrs extract` into
`_research/oracle-runs/_wdl-survey/`, counts and shapes only):

| World | `EACH_SEC` slots | Use |
|-------|------------------|-----|
| `TITLE` | 1 (2 arms, 6 clears) | the logo screen's 12-second and the splash's 4-second countdowns; each counts a skill down once per run, `END`s while it is above zero, then clears its slot and `BRANCH`es to the next screen |
| `HEART`, `LEGALISM`, `NEWAGE` | 1 | a three-second countdown after the level's fade-in whose last run `CALL`s the level's game initialization and shows the control panel |
| `LEGALISM` | 9, 10 | a 10-second trap countdown (`BRANCH` at zero) and a once-per-second check that repeats until a region exit clears it |
| `APATHY`, `START` | none | |

No `EACH_SEC` action in the retail scripts uses `WAIT`. The title arms its logo countdown in the
same action as `PLAY_FLICFILE <logo.flc>`, after the instruction. `TITLE`, `HEART`, `LEGALISM` and
`NEWAGE` stalled without it: the title stayed on the logo, and the three levels never reached
their game initialization (the missing HUD, crosshair and weapon noted in the roadmap).

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: 1998 A3 manual p.96 (`EACH_TICK`: 16 slots, run after every frame
  cycle in order, `SET EACH_TICK.n,Action|NULL`, no `WAIT`), p.117 (`TICKS` +1 per 1/16 s, `SECS`
  +1 per second), p.82 (`PLAY_FLIC`: the global `EACH_TICK` actions keep running), p.80 (`WAIT`,
  `WAITT`), p.88–89 (`PRINTFILE`, `PRINT_STRING`, `PRINT_VALUE`). The 2001 A4 manual has neither
  `EACH_SEC` nor `EACH_TICK`. The keyword `EACH_SEC` appears in the retail and patched `VRUN.EXE`
  binaries next to `EACH_TICK`, `EACH_CYCLE` and `EACH_LIST` (byte presence only; nothing was
  disassembled). The retail WDL only to count and classify `EACH_SEC` uses; no script text is
  reproduced here. No source-level material.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261015-each-sec-cadence/` (generated
  carriers, raw `OV*.TXT`/`OV*.PCX` outputs, `launches.md`), `_research/oracle-runs/_wdl-survey/`.
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the copy used by
  OV-20261009-shoot-mapping and OV-20261015-branch-tail-jump).
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing
  under `_research/` is committed.

Before this run compile validity was not established: the earlier `scheduler-if-start-cycle`
synthetic script also contained `SET EACH_SEC.1`, but every launch of it failed on unrelated
Genesis errors, and the launch that succeeded was the instrumented retail carrier, which has no
`EACH_SEC` in its markers.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X, `forcerate=60`, `aspect=false`, `output=surface`,
  `memsize=32`; carrier `work/` mounted as C:, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`
  (the launcher of the projection, SHOOT and BRANCH probes).
- Fixture generator: `ovtool oracle prepare each-sec-cadence --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/EachSecProbeCases.cs`). The `face` room of the projection probes only
  gives the runtime a valid world. One carrier per case.
- Trace channel: after its usual settling `WAITT 16` and skill dump, `IF_START` selects
  `OVSEC0.TXT` (`PRINTFILE "ovsec", 0`), writes `BEGIN` and runs the case. Every line is a token
  followed by `TICKS` (and `SECS`) when it was printed. At `forcerate=60` a frame advances `TICKS`
  by about 0.267, so equal printed values mean the same frame. Every case clears its slots
  before `RET`.
- Cases (`t` = `TICKS` on the `ARM` line):
  - `cadence`: `ARM`; `SET EACH_SEC.1, sec_a`; `WAITT 40`; clear; `RET`. `sec_a` prints `S TICKS SECS`.
  - `phase`: the same after an extra `WAITT 8`, so the arm falls 8 ticks later in the second.
  - `order-vs-tick`: `SET EACH_TICK.1, tick_k` (prints `K TICKS` every frame) and
    `SET EACH_SEC.1, sec_a`; `WAITT 20`; clear both; `RET`.
  - `slot-16`, `slot-17`: `SET EACH_SEC.n, sec_a`; `WAITT 20`; clear; `RET`.
  - `wait-inside`: `sec_w` prints `W1 TICKS`, waits `WAITT 24`, prints `W2 TICKS`; armed for
    `WAITT 56`, cleared, `RET`, then `WAITT 32` and `LATE` so waiting instances can finish.

## Observations
All runs 2026-10-15 23:19–23:24 UTC: six launches via `run-all.sh`, then `order-vs-tick` twice
more and `cadence` once more via their `run.sh`. Every launch but `slot-17` exited with rc 0, all
outputs present and no compiler error in `OVCON.TXT`. Values are `PRINT_VALUE` output (three
decimals); each line is `token TICKS [SECS]`.

| Case | Trace in `OVSEC0.TXT` | Reading |
|------|-----------------------|---------|
| `cadence` (1st launch) | `ARM 14.344 0.000`, `S 15.125 1.000`, `S 31.625 2.000`, `S 48.156 3.000`, `RET` | accepted; runs once per second and repeats until cleared; the first run came 0.8 ticks after the `SET`, exactly when `SECS` went up (shared boundary) |
| `cadence` (rerun) | `ARM 24.000 1.000`, `S 32.000 2.000`, `S 48.000 3.000`, `RET` | the same, 8 ticks after the `SET` |
| `phase` | `ARM 32.000 1.000`, `S 32.000 2.000`, `S 48.000 3.000`, `S 64.000 4.000`, `RET` | the first run came in the **same frame** as the `SET` (equal `TICKS`), when `SECS` went up; per-arm would put it at 48. `SECS` read 1 in the resumed action and 2 in the slot action of that frame |
| `order-vs-tick` (3 launches, identical) | `ARM 24.000 1.000`, `K 24.000`, `S 32.000 2.000`, `K 32.000`, `K 40.000`, `RET` | in the frame where both ran, `EACH_SEC` ran **before** the global `EACH_TICK` slot |
| `slot-16` | `ARM 24.000 1.000`, `S 32.000 2.000`, `RET` | slot 16 accepted and run |
| `slot-17` | no `OVSEC0.TXT`; `OVCON.TXT`: `Error(41): Range over/underrun 17 parameter 1< or >16` on both `SET EACH_SEC.17` lines, "Genesis failed", 2 errors; launcher rc 3 | exactly 16 slots, checked by the compiler |
| `wait-inside` | `ARM 24.000 1.000`, `W1 32.000`, `W1 48.000`, `W2 56.000`, `W1 64.000`, `W2 72.000`, `RET`, `W2 88.000`, `LATE` | each second started a new instance while earlier ones waited; every `W2` came 24 ticks after its own `W1` |
| every trace | `SECS` printed 0.000, 1.000, 2.000 … at every point, including mid-second | `SECS` is a whole number of seconds |
| harness | in the first `cadence` launch `TICKS` advanced in fractions; in every later launch it advanced by exactly 8 per frame (`order-vs-tick` printed only three `K` lines in 20 ticks) | the runtime ran at about 2 frames per second in those launches; the readings above do not depend on it |

## Conclusion
Confirmed for patched V3.963:

1. **`EACH_SEC.n` is a list of 16 global slots** (`SET EACH_SEC.n, action|NULL`, n = 1–16). Slot
   numbers outside 1–16 are a compile error (Error 41). An assigned action runs **once per
   second, again every second** until the slot is cleared.
2. **All slots share the runtime's second.** The slots run in the frame where `SECS` goes up, not
   one second after their `SET`: armed 0.8 ticks before a boundary the first run came 0.8 ticks
   later, and armed in the boundary frame itself it ran in that same frame.
3. **`SECS` counts whole seconds.** It goes up after the frame's resumed `WAIT` actions and before
   the frame's `EACH_SEC` actions.
4. **Order:** in a frame where both run, the `EACH_SEC` slots run **before** the global `EACH_TICK` slots.
5. **`WAIT` inside an `EACH_SEC` action is allowed:** each second starts another instance of the
   action, and waiting instances finish on their own.

## Confidence
high for 1, 2, 3 and 5: every trace matches exactly one predicted reading, the `slot-17` compile
error is explicit, and the shared-boundary reading shows in three independent arm times (0.8, 8
and 0 ticks before the boundary). medium for 4: three identical launches, but at 8 ticks per frame
each trace holds only one frame where both lists ran.

## Open Questions
- The flic case: the title arms its countdown *after* `PLAY_FLICFILE` in the same action. In
  OpenVirtue that instruction holds the action until the flic ends, so the question does not
  affect the title there. Whether the original's `PLAY_FLICFILE` blocks the action is itself open
  (roadmap); a stopwatch run of the retail title (splash 12 s after the logo starts, or 12 s after
  it ends) would answer both. There is no synthetic FLC generator, so no synthetic case exists.
- Whether `TICKS` and `SECS` come from one clock. In the first `cadence` launch a second spanned
  16.5 ticks (15.125 → 31.625 → 48.156); in the later launches exactly 16. OpenVirtue derives both
  from the same capped frame time, so one second is always 16 ticks there. A run at another
  `forcerate`, or with a heavier scene, would show whether `SECS` follows the PC clock.
- Why the runtime ran at 8 ticks per frame in all but the first launch (DOSBox-X host load, or a
  `TIME_CORR` cap) is not known; it does not affect the readings.
- Where exactly `EACH_SEC` sits relative to region, proximity and animation events in the frame.

## Tests To Add Or Update
- `WdlRuntimeEachSecTests`: cadence on every 16th tick with `SECS` counting whole seconds,
  `SECS` rising after resumed waits and right before the slots
  (`Tick_SecsRisesAfterResumedWaitsAndRightBeforeTheEachSecSlots`), uneven frames, the one-second
  cap on a stall, `NULL` clears, slot order before the global `EACH_TICK` slots
  (`Tick_EachSec_RunsSlotsInOrderBeforeTheGlobalEachTickSlots`), out-of-range slots, read-back,
  `TickFlic`, snapshot phase, level declarations, the countdown-then-`BRANCH` shape, `WAITT` inside
  an action, and the shared boundary (`Tick_EachSecArmedMidSecond_RunsOnTheSharedSecondBoundary`);
  a guarded retail test that `HEART`, `LEGALISM` and `NEWAGE` show their control panel after the fade-in.
- `WdlRuntimeTests.Tick_SetsTimeCorrectionSkill` and
  `Tick_AccumulatesDocumentedTimingSkillsBeforeScheduledActions`: `SECS` stays 0 within the first second.
- `WdlInterpreterTests.Set_SchedulerSlotTargets_ReachTheMatchingSchedulerSlot`,
  `LevelLoaderTests.LoadCore_CapturesInitialEachSecActions`,
  `WdlCoverageAnalyzerTests.Analyze_CountsEachSecDeclarationsAndReassignmentsApartFromEachTick`.
- `RetailSmokeTests.Title_KeyboardThroughMenu_HandsOffToApathyAtItsDeclaredStart`: no key on the
  logo; the splash comes 10–14 engine seconds after the countdown is armed.

## Implementation Notes
Implemented 2026-10-15 (issue #8). `WdlRuntime` keeps 16 `EACH_SEC` slots next to the 16
`EACH_TICK` slots and one runtime second, fed by the same capped frame time as `TICKS`. In
`Tick`, after resumed waits, events, animation and actor targets: when the second completes,
`SECS` goes up by one and every occupied `EACH_SEC` slot runs in order; then the global
`EACH_TICK` slots run. A frame counts at most one second, so a stall never causes a burst of runs.
A slot action that waits suspends like any action, and the slot starts another instance a second
later. `TickFlic` keeps `SECS` counting and, until the flic question is answered, runs the slots too
(`EachSecRunsDuringFlic`). `SET EACH_SEC.n` outside 1–16 is ignored at run time (the original
rejects it at compile time, so valid scripts never contain one). A top-level
`EACH_SEC a, NULL, b;` fills slots in order, like `EACH_TICK`. The slots and the phase of the
second are part of the harness snapshot, and a new level starts with its own declared slots.
