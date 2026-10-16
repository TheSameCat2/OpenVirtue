# OV-20261015-flic-play-blocking: Does PLAY_FLICFILE hold its action, and can a key stop the flic?
Date: 2026-10-15
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
The 1998 A3 reference manual (p.82–83) says that `PLAY_FLIC`/`PLAY_FLICFILE` play a full-screen
flic, that screen building and movement stop meanwhile, and that the 16 global `EACH_TICK`
actions keep running and can compare `FLIC_FRAME` to start sounds. `STOP_FLIC` stops the flic.
The manual does not say:

1. Does the instruction after `PLAY_FLICFILE` run before the flic ends, or does the action wait?
2. Do `IF_ANYKEY` and the other key hooks fire while the flic plays?
3. Does `STOP_FLIC` from such a hook end the flic?
4. Which actions keep running during the flic: global `EACH_TICK` (documented), global
   `EACH_SEC` (OV-20261015-each-sec-cadence open question), and actions resuming from `WAITT`?
5. What does `FLIC_FRAME` read when the flic starts, while it plays, and after it ends or is stopped?

## Why It Matters
GitHub issue #9. The retail title's logo action plays the logo flic, starts the logo music, arms
`IF_ANYKEY` to an action that calls `STOP_FLIC`, and arms a 12-second `EACH_SEC` timeout, all in
that order in one action. OpenVirtue suspended the action at `PLAY_FLICFILE` until the flic
ended. So the music started after the animation, a key during the 13-second logo did nothing,
and the `STOP_FLIC` in the key action could never matter. The script reads as if the instruction
returns at once.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: 1998 A3 manual p.82–83 (`PLAY_FLIC`, `PLAY_FLICFILE`,
  `STOP_FLIC`, `FLIC_FRAME`), p.96–97 (`EACH_TICK`, `IF_ANYKEY`), p.80 (`WAITT`), p.88–89
  (`PRINTFILE`, `PRINT_STRING`, `PRINT_VALUE`). The retail title WDL only to describe the logo
  action's order (no script text is reproduced here). The retail logo flic only for a structural
  count of its chunk types (palette + byte-run first frame, line-delta frames, empty frames, a ring
  frame), so the synthetic fixture uses chunks the runtime is known to play. No pixel data was
  read, and no source-level material.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261015-flic-play-blocking/` (generated
  carriers, raw `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the same copy as
  OV-20261015-branch-tail-jump); generated `PRBFLC.FLC`
  `7b33358b5ff651de00ef76f390e3934ac41f9fd855553db2668e64540420f14b`.
- Confirmation that no proprietary code/assets are committed: synthetic level and synthetic
  flic only; nothing under `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963 (banner "professional release V3.963 - May 30 1998").
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`; carrier `work/` mounted as C:, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. The key cases run `AUTOTYPE -w 25 -p 0.5 space`
  in the autoexec right before the runtime: DOSBox-X presses Space 25 s after that line, about
  7 s after the probe action starts. A first `key-control` launch with `-w 15` pressed the key
  during boot, before the hook was armed, and its trace had no `KEY` line.
- Fixture generator: `ovtool oracle prepare flic-play --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/FlicProbeCases.cs`, flic from `SyntheticFlic.cs`). The flic is
  320x200, 430 frames at 70 ms (about 30 s): a solid frame, then one scanline repainted per
  frame, then a ring frame.
- Trace channel: after its usual settling `WAITT 16`, `IF_START` selects `OVFLC0.TXT`, writes
  `BEGIN` and runs the case. Every line is a token followed by `FLIC_FRAME` and `TICKS` at the
  moment it was printed. A global `EACH_TICK.1` sampler prints `TK` every 32 ticks and `FR` every
  50 flic frames; an `EACH_SEC.1` action prints `SEC`.
- Cases:
  - `key-control`: no flic. Arm `IF_ANYKEY` to an action printing `KEY`, `WAITT 560`, `RET`.
  - `no-key`: arm both samplers, `CALL` an action that does `WAITT 32` then prints `WAITED`,
    print `PLAY`, `PLAY_FLICFILE`, print `AFTER`, `WAITT 560`, `RET`.
  - `key-before`: `EACH_TICK` sampler, `IF_ANYKEY` armed to an action printing `KEY`, then
    `STOP_FLIC`, then `STOPPED`; then `PLAY`, `PLAY_FLICFILE`, `AFTER`, `WAITT 560`, `RET`.
  - `retail-order`: the same, but `IF_ANYKEY` is armed after `PLAY_FLICFILE`, as in the title.

## Observations
All runs 2026-10-16 00:05–00:15 UTC (the evening of 2026-10-15 local time, the date of the ID):
six launches: `key-control` at `-w 15`, `key-control` at `-w 25`, then all four cases via
`run-all.sh` (so `key-control` at `-w 25` twice, with identical traces). All exited with rc 0, all outputs
present and no compiler error in `OVCON.TXT`. Outside a flic this carrier runs at 2 frames per
second in this emulator setup (banner line), so `TICKS` steps by 8 between world frames; during
the flic it steps by about 1.1 per flic frame (70 ms).

| Case | Trace in `OVFLC0.TXT` (token FLIC_FRAME TICKS) | Reading |
|------|-----------------------------------------------|---------|
| `key-control` (`-w 15`) | `ARM 24`, `RET 0 584` | the key came during boot, before the hook (harness, not a finding) |
| `key-control` (`-w 25`, twice) | `ARM 24`, `KEY 0 88`, `RET 0 584` | the scripted Space reaches `IF_ANYKEY` |
| `no-key` | `PLAY 0 24`, `AFTER 1 24`, `TK 1 24`, `FR 1 24`, `SEC 8 32.8`, `WAITED 22 48.2`, `SEC 23 49.3`, then `TK` every 30 frames, `FR` every 50 and `SEC` every 15 frames up to `SEC 428 492.2`; then `SEC 0 510.8` and on, `RET 0 590.8` | the action went on in the same frame; `FLIC_FRAME` was already 1; the `WAITT 32` started before the flic resumed mid-flic (frame 22); `EACH_TICK` and `EACH_SEC` ran throughout; `FLIC_FRAME` is 0 after the end |
| `key-before` | `PLAY 0 24`, `AFTER 1 24`, `TK`/`FR` up to `FR 101 134.6`, `KEY 115 149.9`, `STOPPED 115 149.9`, then `TK 0 158.3` and every 40 ticks, `RET 0 590.3` | the key fired the hook mid-flic; `STOP_FLIC` ended it by the next frame (`FLIC_FRAME` 0, no more `FR`, world frame steps again) |
| `retail-order` | identical to `key-before` | the hook armed after `PLAY_FLICFILE` was armed in the same frame, so the key stopped the flic |

Two timing details:
- The `WAITT 560` that spans the flic ended on time (armed at 24, `RET` at the first world frame
  after 584). The `WAITT 32` in the `CALL`ed action resumed after 24.2 ticks of `TICKS` (21 flic
  frames), not 32. How `WAITT` counts during flic frames was not separated from `TICKS` further.
- The `SEC` lines during the flic are 16.4 ticks (15 frames) apart, once per second.

The end-of-run screenshots (`SCREENSHOT` after the flic in every case) carry the probe world's
palette, not the flic's, so the world palette is restored when the flic ends or is stopped.

## Conclusion
Confirmed for patched V3.963:

1. **`PLAY_FLICFILE` starts the flic and the action goes on.** The next instruction runs in the
   same frame. The instruction does not wait for the flic.
2. **During the flic the scheduler keeps running.** The global `EACH_TICK` slots run once per flic
   frame, the global `EACH_SEC` slots once per second, and actions in `WAITT` resume and go on.
   `TICKS` advances by the flic's frame time.
3. **Key hooks fire during the flic.** `IF_ANYKEY` runs its action while the flic plays,
   whether it was armed before or after `PLAY_FLICFILE`.
4. **`STOP_FLIC` ends the flic.** From the next frame on the world is drawn again with its
   palette, and `FLIC_FRAME` reads 0. Within the action that called `STOP_FLIC`, `FLIC_FRAME`
   still reads the frame that was showing.
5. **`FLIC_FRAME` is the 1-based number of the frame on screen.** It is already 1 right after
   `PLAY_FLICFILE`, counts up to the flic's last frame, and is 0 after the flic ends or is stopped.

So the retail logo is skippable with any key, its music starts with the animation, and its
12-second timeout is counted from the start of the logo.

## Confidence
high for rules 1–5: every case matches exactly one predicted reading, the two key cases agree,
and `key-control` shows the key path works without a flic. medium for the exact `WAITT` rate during
a flic (one sample, 24 ticks for a `WAITT 32`).

## Open Questions
Answered by OV-20261015-flic-scheduler-detail, except `PLAY_FLIC` with a `FLIC` object and an
invalid file. There, the 24.2-tick `WAITT 32` came from its 2-frames-per-second arming frame,
which counts toward the wait.

- How `WAITT` counts during flic frames: the one sample ran faster than `TICKS`. `WAIT` (frame
  cycles) during a flic was not probed.
- Object `EACH_TICK`/`EACH_CYCLE` actions, region and proximity events, and actor movement during a
  flic were not probed. The manual says that movement stops.
- `PLAY_FLIC` with a `FLIC` keyword object, a second `PLAY_FLICFILE` while one plays, and a missing
  or invalid file were not probed.
- Whether a mouse click (`IF_LEFT`, panel buttons) is delivered during a flic.

## Tests To Add Or Update
- `WdlRuntimeTests.Runtime_FlicInstructions_RecordPlaybackAndStopRequests`: the statement after
  `PLAY_FLICFILE` runs at once and `FLIC_FRAME` reads 1; no action waits on the flic.
- `WdlRuntimeTests`: the retail shape (hook armed after `PLAY_FLICFILE`, the hook calls `STOP_FLIC`).
- `WdlRuntimeTests.TickFlic_RunsGlobalSchedulerButPausesWorldAndObjectTicks`: a `WAITT` action
  resumes during `TickFlic`; `EACH_SEC` runs during `TickFlic` (`WdlRuntimeEachSecTests`).
- `RuntimeHostFlicTests` (new): a key during the flic runs the hook, `STOP_FLIC` ends playback,
  and `FLIC_FRAME` is 1-based during play and 0 afterwards.
- `RetailSmokeTests.Title_KeyboardThroughMenu_HandsOffToApathyAtItsDeclaredStart`: Space while
  the logo flic plays shows the splash right away.

## Implementation Notes
`PLAY_FLICFILE` in `WdlInterpreter` records the host request and goes on to the next instruction.
The runtime keeps no flic-waiting actions, so `CompleteFlic` and `FlicWaitingActionCount` are gone,
and it sets `FLIC_FRAME` to 1 when it records the request. `WdlRuntime.TickFlic` now resumes
waiting actions before the second and the global slots, in the same order as `Tick`, and runs the
`EACH_SEC` slots unconditionally. Key hooks already fired during a flic in `RuntimeHost`. The host
sets `FLIC_FRAME` to the 1-based frame number while it plays and to 0 when the flic ends, is
stopped, or cannot be played. The rest of this paragraph is superseded by
OV-20261015-flic-scheduler-detail, which measured one scheduler cycle per flic frame and the
`WAITT` count. It used to read: OpenVirtue runs the global slots once per host frame during a
flic, not once per flic frame. `WAITT` counts `TICKS` during a flic, because the faster rate
seen here rests on a single sample.
