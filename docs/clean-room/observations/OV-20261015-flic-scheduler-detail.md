# OV-20261015-flic-scheduler-detail: How waits, the world and input behave while a flic plays
Date: 2026-10-15
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261015-flic-play-blocking showed that `PLAY_FLICFILE` does not hold its action and that the
global scheduler, `WAITT` actions and key hooks keep running during a flic. It left these open:

1. How do `WAITT n` (ticks) and `WAIT n` (frame cycles) count during flic frames? The one sample
   had a `WAITT 32` resume after 24.2 `TICKS`. Does it matter whether the wait started before
   or during the flic?
2. Do object and region `EACH_TICK`/`EACH_CYCLE` actions, region and proximity events, and
   actor and player movement run during a flic? The manual (p.82) says that movement stops.
3. Is a mouse click (`IF_LEFT`) delivered during a flic?
4. What does a second `PLAY_FLICFILE` do while one plays? And what does a missing flic file do
   (`FLIC_FRAME`, and whether the action goes on)?

## Why It Matters
The retail title plays its logo flic and arms timeouts and hooks around it. OpenVirtue ran the
global scheduler once per host frame during a flic and counted `WAITT` in `TICKS`. It also left
the open points above to guesses. The `WAITT` rule turned out to apply to every frame, not only
to flic frames.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.37 (`CYCLES`, `DELAY`), p.64–70 (actor
  `TARGET`, `SPEED`, `DIST`, `IF_NEAR`, `EACH_TICK`, `EACH_CYCLE`, `LIBER`), p.80 (`WAIT`, `WAITT`),
  p.82–83 (`PLAY_FLICFILE`, `STOP_FLIC`), p.96–97 (`IF_LEFT`), p.98–99 (synonyms), p.104
  (`PLAYER_VX`, `MOVE_MODE`), the WMP format chapter (`ACTOR`/`THING` lines). No retail script,
  asset or source-level material.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261015-flic-scheduler-detail/` (carriers,
  raw `OV*.TXT`/`OV*.PCX` outputs, emulator and click-helper logs, `launches.md`; the follow-up
  rounds in `round2/` and `round3/` with their own `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the same copy as
  OV-20261015-flic-play-blocking); generated `PRBFLC.FLC`
  `7b33358b5ff651de00ef76f390e3934ac41f9fd855553db2668e64540420f14b` (unchanged from that probe),
  `PRBFL2.FLC` `0c54598f9d480e7b1a61202a3a199635bc4880e1d3a8eee544a3e17d58ac0b76`,
  `PRBFL3.FLC` `fc06edd91ecfa912cab4e7c4bc8703f6e9c708b9c221596fd59ecdd6062fca1a`.
- Confirmation that no proprietary code/assets are committed: synthetic level and synthetic
  flics only; nothing under `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963.
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`, launched as `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. These
  carriers ran at about 61 frames per second (`TIME_CORR` 0.25–0.28 outside a flic). The first
  flic probe's carriers ran at 2 frames per second.
- Fixture generator: `ovtool oracle prepare flic-detail --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/FlicDetailProbeCases.cs`). Flics come from `SyntheticFlic.cs`:
  `PRBFLC.FLC` (430 frames at 70 ms), `PRBFL2.FLC` (120 frames at 70 ms, different colours) and
  `PRBFL3.FLC` (200 frames at 40 ms). The `world` cases add a FOLLOW actor and a second region
  (`pad`, an 8 × 8 island bounded by invisible passable walls); the `near` cases add a THING.
- Mouse input: DOSBox-X `AUTOTYPE -list` (recorded in `mouse-control`) names keys only, with no
  mouse buttons. So `run.sh` starts `tools/oracle/x11_click.py`, which waits 25 s and then sends
  one button-1 press/release to the DOSBox-X window with X11 `XSendEvent`. It does not move the
  host pointer.
- Trace channel: as in the first probe, `IF_START` selects `OVFLD0.TXT` after its settling
  `WAITT 16`. A global `EACH_TICK.1` sampler counts its own runs in `fd_n`. Every line is a token
  followed by `FLIC_FRAME`, `TICKS` and `fd_n` when it was printed, then the case's extra skills.
  The sampler prints `TK` (with `TIME_CORR`) every 32 ticks and `FR` every 25 or 50 flic frames.
- Cases (each flic case has a no-flic control with the same script):
  - `waits` / `waits-control`: `CALL`s a `WAITT 32` and a `WAIT 16` before `PLAY_FLICFILE`, and
    the same pair right after it in the same frame. A chain action then runs `WAITT 40, 32, 64,
    16`, `WAIT 16, 64, 1` and `WAITT 1`, printing after each, so most intervals start and end
    inside the flic. The main action itself waits `WAITT 32`, then `WAITT 868`.
  - `world` / `world-control`: a FOLLOW actor (`SPEED 0.2`, `DIST 6`, `LIBER`) with its own
    `EACH_TICK` (count), `EACH_CYCLE` (two 4-tick phases, count) and `IF_NEAR`. The room region
    has its own `EACH_TICK` (count). 64 ticks after `PLAY_FLICFILE` the player is beamed into the
    pad with `SET PLAYER_X/PLAYER_Y`, and the pad has `IF_ENTER`/`IF_LEAVE`. The sampler reads the
    actor's position through a synonym.
  - `walk` / `walk-control` (round 2): the sampler sets `PLAYER_VX -0.1`, `PLAYER_VY 0.2` every
    cycle, so the player walks through the pad.
  - `near` / `near-control` (round 2): 64 ticks after `PLAY_FLICFILE` the player is beamed 4 steps
    from a THING with `DIST 6` and a counting `IF_NEAR`.
  - `mouse` / `mouse-control`: `IF_LEFT` armed to an action printing `LEFT`; one click at 25 s.
  - `replace`: 48 ticks into the 430-frame flic, `PLAY_FLICFILE <PRBFL2.FLC>`, then `WAITT 8` and
    a `SCREENSHOT`.
  - `missing` (rounds 1 and 2) and `missing-during` (round 2): `PLAY_FLICFILE` of a file that is not
    there, with no flic playing and 48 ticks into a flic. Round 2 writes the trace out right
    before and after the bad instruction (switching `PRINTFILE` closes the file;
    re-selecting appends, OV-20261009-shoot-mapping).
  - `speed` (round 3): the 40 ms flic.

## Observations
All runs 2026-10-16 00:41–00:55 UTC (the evening of 2026-10-15 local time, the date of the ID):
15 launches, one per case (8 in round 1, 6 in round 2, 1 in round 3), no compiler error in any
`OVCON.TXT`. All exited with rc 0 except round-1 `missing` and round-2 `missing-during`. Those
two hit a DOSBox-X "Triple fault" and exited with rc 1 and no completion marker. Below, cycle 0
is the frame that ran `PLAY_FLICFILE`.

| Case | Trace (token FLIC_FRAME TICKS fd_n) | Reading |
|------|-------------------------------------|---------|
| `waits-control` | `PLAY 0 13.375 0`; `WB16`/`WD16 0 17.594 16`; `WA32`/`WC32`/`M32 0 44.563 119`; chain `S0 52.438 149`, `S1 83.625 268`, `S2 146.813 509`, `S3 162.000 567`, `S4 166.219 583`, `S5 182.969 647`, `S6 183.250 648`, `S7 183.500 649` | `WAIT n` resumes n cycles after the cycle it was armed in. `WAITT 40, 32, 64, 16` resume 39.06, 31.19, 63.19, 15.19 `TICKS` after they were armed, and `WAITT 1` one 0.25-tick cycle later |
| `waits` | `PLAY 0 13.438 0`, `AFTER 1 13.438 0`; `TK` lines in the flic every 30 frames, `TIME_CORR 1.094`, `fd_n` = `FLIC_FRAME` + 1; `WB16`/`WD16 16 30.938 16`; `WA32`/`WC32`/`M32 30 46.250 30`; `S0 37`, `S1 66`, `S2 124`, `S3 138`, `S4 154`, `S5 218`, `S6 219`, `S7 220` (frames); after the flic `TIME_CORR` 0.25–0.28 again; `RET 0 912.550` | one scheduler cycle per flic frame, `TICKS` + 1.09375 each. Cycle k shows frame k. `WAIT 16, 64, 1` take 16, 64, 1 flic frames. `WAITT 32, 64, 16` armed inside the flic take 29, 58, 14 frames (31.72, 63.44, 15.31 ticks). Waits armed in cycle 0 take one frame more than the 1.09-tick frames alone would give |
| `world-control` | `fd_an`, `fd_rn` rise by 123 per 123 cycles; `fd_cn` by 103 over 800 ticks; actor closes on the player at 0.2 steps per tick; `TELED -8 16`, no `ENTER`; `NEAR` every cycle from `TICKS` 323.4 while the actor pushes the player (307 runs) | object and region actions run each cycle. Beaming the player into a region does not fire `IF_ENTER`. `IF_NEAR` runs in every cycle the player is within `DIST` |
| `world` | from `PLAY` to the end of the flic every `TK` line reads `fd_an 47`, `fd_cn 2`, `fd_rn 47`, actor at (37.876, −11.382) while `fd_n` goes 1 → 421. `TELED 59 77.813` sticks (`PLAYER_X -8`); first `AT` line `0 483.831`, then everything moves again; `NEAR` only after the flic | the actor's `EACH_TICK` and `EACH_CYCLE`, the region's `EACH_TICK` and FOLLOW movement are all paused during the flic, and go on from the same state after it. Skill writes to the player position work |
| `walk-control` | player at (−3.19, 6.41) at `TICKS` 45.6; `ENTER 0 73.938 232 -5.991 12.032`; `LEAVE 0 114.281 386 -9.985 20.054` | walking fires `IF_ENTER`/`IF_LEAVE` |
| `walk` | player at (0, 0) in every `TK` line while `FLIC_FRAME` > 0; then moving; `ENTER 0 544.487`, `LEAVE 0 584.862` | player movement is paused during the flic (p.82), so no region event fires in it |
| `near-control` | `BEAMED 0 76.531 241 24 -8`, `NEAR 0 76.531 242 1` (same frame, after the global slot); the player is pushed to (24.004, −2.797); one `NEAR` in all | beaming within `DIST` fires `IF_NEAR` in the same cycle |
| `near` | `BEAMED 59 77.875 59 24 -8`; first `NEAR 0 483.894 433` | proximity events are not dispatched during the flic. `IF_NEAR` fires in the first world cycle after it |
| `mouse-control` | `ARM 0 13.531`, `LEFT 0 312.281`, `RET`; click log: press at 25.00 s, release at 25.25 s | the host click reaches `IF_LEFT` |
| `mouse` | `LEFT 272 310.813 272`, `FR` lines go on to 401 | `IF_LEFT` fires during the flic, and the flic goes on |
| `replace` | `P2 44 61.469 44`, `P2A 1 61.469 44`, `SHOT 7 69.125 51`, `FR 101 171.938 146`, then `TK 0 209.425` | the second flic replaces the first. `FLIC_FRAME` is 1 in the requesting cycle, the next cycle shows its frame 1, and it ends after its 120 frames. The `SCREENSHOT` taken during the flic holds the world view (palette index 9, the ceiling colour), not the flic |
| `missing` (round 2) | `PLAY 0 20.469 27`, `AFTER 0 20.469 27`, `W32 0 51.594 133`, `RET`; rc 0 | with no flic playing, a missing file is skipped. `FLIC_FRAME` stays 0 and the action goes on |
| `missing-during` | `P2 44 61.406 44`, `P2A 44 61.406 44`, then nothing; "Triple fault" | the action goes on and `FLIC_FRAME` keeps the playing frame. The emulated CPU then faults within 32 ticks, before the next flushed line |
| `missing` (round 1) | `OVFLD0.TXT` empty; "Triple fault" | the same fault (that case also requested a missing file during a flic); the unflushed trace was lost |
| `speed` | in the flic `TK` lines every 52 frames, `TIME_CORR 0.625`, `TICKS` + 32.5 per 52 frames | `TIME_CORR` during a flic is the frame delay over 64 ms (40/64), not over the 62.5 ms tick (0.64) |

The `waits` counts give one rule for both waits, in world frames and in flic frames alike.
Count down every waiting action at the end of each cycle: by one for `WAIT`, by the cycle's
`TIME_CORR` for `WAITT`. The cycle the wait was armed in counts too. At the start of a later
cycle the action resumes once its count is below 1. So `WAIT n` resumes n cycles after its
arming cycle. `WAITT n` resumes in the first cycle where the time counted from the start of its
arming cycle exceeds n − 1 ticks. Checks:
- At 60 fps, `WAITT 32` resumes after 31.19 ticks and `WAITT 1` after one frame.
- Inside the flic, `WAITT 32` armed at frame 37 counts 28 × 1.09375 = 30.6 (not more than 31) at
  frame 65 and 31.7 at frame 66, and resumes at 66.
- `WAITT 32` armed in cycle 0 also counts cycle 0's 0.281 ticks: 0.281 + 28 × 1.094 = 30.9 at
  cycle 29 and 32.0 at cycle 30, so it resumes at 30 (observed). This is the one-frame-later
  effect.
- The first probe's `WAITT 32` resumed 24.2 `TICKS` in because its arming frame, at 2 frames per
  second, counted 8 ticks.
- The `WAITT 868` that ends after the flic fits too.

The `EACH_SEC` lines of the first probe (every 15 flic frames) also follow from 70/64 ticks per
frame: a second is 14.6 such frames.

## Conclusion
Confirmed for patched V3.963:

1. **A flic runs one scheduler cycle per flic frame.** In cycle k, `FLIC_FRAME` reads k and frame
   k is on screen. `TIME_CORR` is the flic's frame delay over 64 ms: 1.09375 at 70 ms, 0.625 at
   40 ms. `TICKS` grows by that each frame, about 2 % slower than real time. The cycle after the
   last frame is a world frame with `FLIC_FRAME` 0.
2. **`WAIT n` counts cycles, flic frames included.** `WAITT n` counts `TIME_CORR`, and its arming
   cycle counts. It resumes once more than n − 1 ticks are counted (see the rule above). This
   holds in world frames too.
3. **During a flic the world is paused.** Player movement (even with `PLAYER_VX/VY` set), actor
   movement, texture animation and `EACH_CYCLE`, object and region `EACH_TICK` actions, region
   events and proximity events all wait. They go on from the same state after the flic. A skill
   write to `PLAYER_X/PLAYER_Y` during the flic sticks, and its `IF_NEAR` fires in the first world
   cycle after it.
4. **Mouse clicks are delivered during a flic.** `IF_LEFT` fires mid-flic, like the key hooks.
5. **A second `PLAY_FLICFILE` replaces the playing flic.** `FLIC_FRAME` is 1 for the rest of the
   requesting cycle, and the new flic's frame 1 is shown in the next cycle.
6. **A missing flic file with no flic playing is skipped.** `FLIC_FRAME` stays 0 and the action
   goes on. During a flic the action also goes on and `FLIC_FRAME` keeps the playing frame, but
   this runtime then faults (a crash in the original, not behavior to reproduce).

Two side observations from the controls: beaming the player with `SET PLAYER_X/PLAYER_Y` does not
fire `IF_ENTER`, while walking does. And `IF_NEAR` ran in every cycle while the actor pushed the
player.

Addendum (2026-10-19): OV-20261019-proximity-events probed both. A beam keeps the player's region
until a wall crossing or `LOCATE`. `IF_NEAR` fires once per crossing of `DIST`, measured as
|dx| + |dy|, and does not repeat while the player stays within it. The 307 runs in `world-control`
came from contact: that actor's `DIST` (6) was below its contact distance (about 9.2), and the hook
ran in every cycle it pushed the player.

## Confidence
high for rules 1–5: each case matches one predicted reading, every flic case has a control, and the
wait rule fits every measured wait (14 per `waits` case) at both frame rates. high for the delay/64 rule (two flic
speeds). medium for rule 6 during a flic, because the trace after the fault was lost.

## Open Questions
- How far the flic plays after a missing file is requested during it, before the fault.
- `PLAY_FLIC` with a `FLIC` keyword object, and an existing but invalid flic file.
- A fractional `WAIT` (`WAIT 1.5`): answered by OV-20261029-scheduler-order round 3. It resumes once
  the count is below 1, and a `WAIT` below 1 does not wait.
- Whether a wait armed by a key or mouse hook counts that cycle: the hooks run early in the cycle
  (`LEFT` printed the same `fd_n` as resumed actions), so OpenVirtue counts it.
- Whether `IF_NEAR` repeating every cycle within `DIST` matches OpenVirtue's proximity dispatch:
  answered by OV-20261019-proximity-events (it does not repeat; the repeat here was contact).

## Tests To Add Or Update
- `WdlRuntimeWaitCountTests` (new): `WAITT` at 0.25-tick frames resumes once more than n − 1 ticks
  are counted; `WAITT 1` resumes the next frame; a `WAIT` armed in a frame resumes n frames later;
  in 70 ms flic frames `WAITT 32/64/16` take 29/58/14 frames and `WAIT 16` 16 frames;
  `Timing.FlicCycleSeconds` gives `TIME_CORR` 1.09375 and 0.625; `EACH_SEC` runs every 15th
  70 ms frame; a missing `PLAY_FLICFILE` keeps `FLIC_FRAME`.
- `WdlRuntimeTests` wait tests: a wait armed between frames (by `RunAction` or a hook) counts the
  next frame, so `WAIT n` resumes in frame n + 1.
- `WdlRuntimeEachSecTests.Tick_SecsRisesAfterResumedWaitsAndRightBeforeTheEachSecSlots`: `WAITT 15`
  from `IF_START` resumes in frame 16.
- `RuntimeHostFlicTests`: one cycle per flic frame with `TIME_CORR` = delay/64; a missing file
  keeps `FLIC_FRAME` at 0 and draws the world; a missing file during a flic keeps it playing.

## Implementation Notes
`WdlRuntime.ResumeWaitingActions` no longer counts down. It resumes, in arming order, the
actions whose count is below 1. `CountDownWaitingActions` runs last in `Tick` and `TickFlic` and
counts every waiting action down by one cycle or by `TIME_CORR`. Actions of inactive entities
still do not count. `Timing.FlicCycleSeconds(delayMs)` (delay/64/16 s) is the frame time for a
flic cycle. `RuntimeHost.RunFlicCycles` replaces the per-host-frame `TickFlic`. Each due flic
frame sets `FLIC_FRAME` to its 1-based number and runs one `TickFlic` cycle. Cycle k is due
k − 1 frame delays after the request, and a host frame catches up at most four cycles. After the
last frame the world frame runs at once. `WdlRuntime.FlicFileExists`, set by the host to an
archive lookup, keeps `PLAY_FLICFILE` of a missing file from setting `FLIC_FRAME` to 1. The host
keeps a playing flic when a requested file is missing or invalid and does not reproduce the
fault. The object/world pause during a flic, mouse hooks during a flic and flic replacement
already matched the original.
