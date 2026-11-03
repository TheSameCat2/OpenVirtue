# tools

Local inspection/extraction utilities built on `OpenVirtue.Formats`. These ship
no game data; they operate on the user's own files.

## OpenVirtue.Tools (`ovtool`)

```
dotnet run --project tools/OpenVirtue.Tools -- wrs list    <archive.wrs>
dotnet run --project tools/OpenVirtue.Tools -- wrs extract <archive.wrs> [output-dir]
dotnet run --project tools/OpenVirtue.Tools -- wrs pack    <input-dir> <archive.wrs>
dotnet run --project tools/OpenVirtue.Tools -- pcx info     <image.pcx>
dotnet run --project tools/OpenVirtue.Tools -- wmp info     <map.wmp>
dotnet run --project tools/OpenVirtue.Tools -- wav info     <sound.wav>
dotnet run --project tools/OpenVirtue.Tools -- save unpack  <file.sav> <body>   # a save to its body
dotnet run --project tools/OpenVirtue.Tools -- save pack    <body> <file.sav>   # and back (the original loads it)
dotnet run --project tools/OpenVirtue.Tools -- save capture <archive.wrs> <file.sav> [main.wdl]   # a level as loaded, in the original's save format
dotnet run --project tools/OpenVirtue.Tools -- save play <archive.wrs> <main.wdl> [--ticks N]     # runs a level headless (0.25 ticks a cycle); its full SAVEs also land beside the archive in the original's format
dotnet run --project tools/OpenVirtue.Tools -- wdl info     <script.wdl>
dotnet run --project tools/OpenVirtue.Tools -- level info   <archive.wrs> [main.wdl]
dotnet run --project tools/OpenVirtue.Tools -- level bench  <archive.wrs> [frames] [main.wdl]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare scheduler-if-start-cycle [output-dir] [--runtime-dir <dir>] [--carrier-dir <dir>] [--dosbox-x <exe>]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare projection-fov|pixel-aspect|tilt-model [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--arcs|--aspects|--tilts a,b,c] [--videos m1,m2]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare shoot-mapping [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>] [--shoot-x a,b] [--shoot-y a,b] [--tilts a,b] [--videos m1,m2]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare branch-semantics [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare each-sec-cadence [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare flic-play [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
dotnet run --project tools/OpenVirtue.Tools -- oracle prepare proximity-events [output-dir] [--runtime <VRUN-patched.EXE>] [--dosbox-x <exe>]
```

- `wrs list` — print each entry (name, compressed/uncompressed size) and a
  summary by file type.
- `wrs extract` — decompress every entry to a folder.
- `wrs pack` — build a local WRS archive from a flat directory using
  deterministic literal-only LZSS payloads.
- `pcx info` — print a PCX image's dimensions and a palette sample.
- `wmp info` — print a map's vertex/region/wall/thing/actor counts and player start.
- `wav info` — print a sound's channels/rate/bit-depth and duration.
- `wdl info` — parse a script and summarize its top-level items by keyword.
- `level info` — load a whole level (WDL + WMP) and report its geometry, placements, and skills.
- `level bench` — run a level headless (no window, no sound) for a number of frames at 60 Hz and
  print the time per frame: a check on the engine's per-frame cost on a retail level.
- `oracle prepare scheduler-if-start-cycle` — create a local-only DOSBox-X probe
  folder under `_research/oracle-runs/` for measuring `IF_START` versus first
  `EACH_TICK` dispatch. Generated WDL/WMP/WRS/runtime/display-asset files are
  ignored and must not be committed; reduce results into
  `docs/clean-room/observations/`.
  Passing `--carrier-dir` also creates a local prepared-title carrier harness
  with active `APPEND_MODE` lines removed from copied WDL files and marker-file
  logging injected into the title script. Marker files use the A3 documented
  five-character base plus number form; patched V3.963 has been observed writing
  one-digit suffixes such as `OVSTA0.TXT`, `OVSCH0.TXT`, and `OVTIK0.TXT`.
  This helper does not run SaintsX setup scripts or external `patch.exe`; oracle
  prep must stay noninteractive and auditable.
- `oracle prepare projection-fov | pixel-aspect | tilt-model` — create the
  local-only carriers for the three OV-20261009 projection probes (horizontal FOV
  vs `PLAYER_ARC`, pixel aspect / `ASPECT` per `VIDEO` mode, `PLAYER_TILT` shear vs
  pitch). One carrier per parameter value, each fully synthetic (generated WDL/WMP,
  flat-color PCX textures and palette) plus the user's patched `VRUN.EXE` copied in
  by path. Every carrier gets a POSIX `run.sh` + `dosbox-x.conf` (native Linux
  DOSBox-X: `forcerate=60`, `aspect=false`, `output=surface`) and a `run.bat` +
  `dosbox.conf` for Windows; `run.sh` parks previous outputs under `work/_stale/`
  (never deletes) and appends to the probe's `launches.md`. The scheduler probe
  also receives `run.sh`/`dosbox-x.conf` now.
- `oracle prepare shoot-mapping` — the OV-20261009-shoot-mapping carriers: the `face`
  room plus three THING targets (map y +12 / 0 / −12 at x = 44, 4 × 16 steps, a 64×256
  half/half bitmap at SCALE 16 so `HIT_X`/`HIT_Y` resolve 1/16 step) with `IF_HIT`
  marker actions, and an asymmetric far-wall face for wall texture orientation. One
  variant per `SHOOT_X` / `SHOOT_Y` / `PLAYER_TILT` value plus a centered baseline;
  `IF_START` fires one `SHOOT` and dumps `HIT_DIST`/`HIT_X`/`HIT_Y`/`RESULT`/`TICKS`
  both inside the hit action (`OVHIT0.TXT`) and right after the instruction
  (`OVSHO0.TXT`). `geometry.json` carries the predicted hit, target and texel under the
  shared-projection model next to the competing readings.
- `oracle prepare branch-semantics` — the OV-20261015-branch-tail-jump carriers: one per
  control-flow case in `BranchProbeCases.cs` (fall-through, `WAIT` in the target, synonym,
  `NULL` synonym, undeclared name, `IF`/`WHILE`, the menu's `IF_EQUAL` chain, depth,
  `EACH_TICK`, plus `CALL` controls). The `face` room only boots the world; the case's
  actions write trace tokens to `OVBRN0.TXT` in execution order, and `case.json` lists the
  trace each reading of `BRANCH` predicts. No measurement script: read the traces.
- `oracle prepare each-sec-cadence` — the OV-20261015-each-sec-cadence carriers, built from
  `EachSecProbeCases.cs` on the same trace scaffold (`TraceProbeCase.cs`): cadence, phase
  (armed off the second boundary), order against `EACH_TICK`, slots 16 and 17, and `WAITT`
  inside an `EACH_SEC` action. Every line in `OVSEC0.TXT` is a token followed by `TICKS`
  (and `SECS`); at `forcerate=60` equal `TICKS` mean the same frame. `case.json` lists the
  trace each reading predicts. No measurement script: read the traces.
- `oracle prepare flic-play` — the OV-20261015-flic-play-blocking carriers (issue #9), built
  from `FlicProbeCases.cs` on the same trace scaffold. Every carrier holds `PRBFLC.FLC`, a
  synthetic 30-second FLC written by `SyntheticFlic.cs` (a line sweeping down a solid screen),
  and plays it with `PLAY_FLICFILE`. The cases ask whether the action goes on past the
  instruction, whether `WAITT`, `EACH_TICK` and `EACH_SEC` run during the flic, and whether a
  key fires `IF_ANYKEY` and `STOP_FLIC` ends the flic. The key cases press Space through a
  DOSBox-X `AUTOTYPE` line in the autoexec (`TraceProbeCase.Autotype`); `key-control` checks
  that the scripted key reaches the runtime at all. Every line in `OVFLC0.TXT` is a token
  followed by `FLIC_FRAME` and `TICKS`.
- `oracle prepare proximity-events` — the OV-20261019-proximity-events carriers, built from
  `ProximityProbeCases.cs` on the same trace scaffold. A global `EACH_TICK` sampler walks the
  player by setting `PLAYER_VX/PLAYER_VY` every cycle. The cases walk past, beam into and out of
  a THING's `DIST`, let a FOLLOW actor approach a player who stands still, and beam into the pad
  island with and without `LOCATE`. Every line in `OVPRX0.TXT` is a token followed by `TICKS`
  and the sampler's run count; `N`/`F` lines are `IF_NEAR`/`IF_FAR` runs with `MY.DISTANCE`.
- `oracle prepare scheduler-order | actor-events | numeric-edge | motion-timing` — the
  OV-20261029 trace probes. `scheduler-order` samples `TICKS`/`TIME_CORR` in every cycle from the top
  of `IF_START` (through `TraceProbeCase.Prelude`) and records the order of global, region, wall,
  thing and actor `EACH_TICK`, `EACH_CYCLE`, `EACH_SEC` and resumed `WAIT`s within a cycle.
  `actor-events` follows `WAY`, `VERTEX`, object, `FOLLOW`/`MOVE` with `CAREFULLY` and `BULLET`
  actors and their `IF_ARRIVED`/`IF_HIT`. `numeric-edge` reads the number format, rounding and what
  division by zero and out-of-domain functions do. `motion-timing` fits `ACCEL` with `FRICTION` and
  `INERTIA` and times texture animation (`DELAY`, `RANDOM`, `ONESHOT`).
- `oracle run <carrier-or-probe-dir> [--rerun] [--visible] [--timeout s]` — launches the prepared
  carriers that have not run yet, one after another, through their own launchers, with SDL's dummy
  video driver unless `--visible`.
- `oracle check <probe> <output-dir> [prepare options]` — prepare, run and diff in one step.
- `oracle summary [oracle-runs-dir]` — replays every run carrier of every probe and prints how many
  match line for line.
- `oracle replay | diff <carrier-or-probe-dir>` — the oracle-diff harness. `replay` packs a carrier's
  `work/` files (not `VRUN.EXE` or the original's `OV*` outputs) into `<carrier>/ov-replay/TITLE.WRS`
  and runs it headless through `RuntimeHost` until the probe's `EXIT` or 64 ticks past the later of
  the original's last sample and the last `TICKS` any of its files prints (the last first-column
  value of a token whose first column never falls), so a sampler switched off early no longer stops
  the replay before `OVEND`; the replay's `PRINTFILE` output lands in
  `<carrier>/ov-replay/openvirtue-prints/TITLE/`. `diff` does the same and compares every `OV*.TXT`
  the original wrote (except the autoexec's `OVDIR.TXT`/`OVCON.TXT`) with the replay's, line by line:
  same token, same field count, numbers within `--tolerance` (default 0.002). Given a probe folder it
  walks every carrier below it (round sub-folders included) and skips carriers that never ran.
  Full saves (`OV*.SAV`) are compared by their unpacked bodies, section by section: the report names
  each differing section, and for the skills section each differing skill. Differences confined to
  what OpenVirtue does not model are set aside and named, and the save still counts as a match: the
  first section's run-time words (+36, +40) and the run-time statistics skills
  (`TIME_*`, `ACTIONS`, `ACTIVE_*`, `MAX_DIST`, `VISIBLES`, `VESA_MODE`, `RANDOM`).
  Timing: the original's `TICKS` per cycle come from one token's column (`--timing TOKEN:COL`,
  detected by default: a token's fractional never-decreasing column before a whole one, so a flic
  carrier's `TICKS` beats the `FLIC_FRAME` printed before it, then the densest token, three lines
  or, failing that, two; a
  whole-number cycle counter on the same lines fills in samplers that print only every few cycles,
  in whole 1/32 ticks; a trace where no token repeats but every line prints `TICKS` and one cycle
  counter, each counted cycle with one `TICKS`, is its own sampler, `*:0`; there the lines of
  every token but the one that leads, whose line shares a `TICKS` with another's counting one
  less, are taken as printed a cycle later: the probe action prints before the sampler counts).
  Until the replay prints its first sampled cycle every frame lasts the original's first sampled
  step. That step, the first sampled cycle's `TIME_CORR`, is read from a column that prints it,
  or else from the step 18 cycles later (where the first sampled step is a frame's): DOSBox-X's
  60 Hz steps repeat every 18 cycles. This
  moved the first difference of the bullet carrier from line 3 to line 284, and added 668 matching
  lines over all carriers; from then on each frame lands `TICKS` on the original's next sample, shifted by the start
  offset, which is reported, and columns that print `TICKS` are compared relative to it. A gap of
  more than four typical steps is taken for a sampler that skipped cycles and run at the typical
  step, unless a cycle counter placed every sample and the typical step is a frame's (at most a
  tick): then it is one long frame of the original's (view-activity's first frames take 8 ticks;
  174 more lines matched over all carriers). A carrier
  without a per-cycle timing column is anchored on the template's start-up stamp (`TCK=` in
  `OVPRB0.TXT`, printed by carriers prepared from 2026-10-29 on) or else on the trace's first line.
  A token's first column is shifted by that stamp when it reads like `TICKS`: never below the stamp,
  never going down, and printed more than once or within a tick of the stamp (so a lone position
  line is compared as it is). `KEY=value` lines compare their numbers, the stamp within one
  start-up cycle.
  Before the first sample the replay plans its start-up in whole 1/32 ticks, so it reaches the
  original's first sample at the original's `TICKS`. Where no column prints the first cycle's
  `TIME_CORR` and the template's stamp lies one frame (8/32 to 10/32 of a tick) before the first
  sample, that gap is the step into it: the probe action starts a frame ahead of the first sample
  in most carriers, and the first cycles' rate cannot tell 8/32 from 9/32 there (below-beam's five
  carriers then match in full). A carrier's `AUTOTYPE` keys are typed into the
  first `INKEY` it opens, held until the frame before the cycle the original's line after the
  input (`R`) counted, with that line and the one before it (`B`) as the clock's two samples
  (OV-20261029-inkey addendum: 10 of 13 carriers); clicks and key hooks are still not replayed.
  A carrier's `case.json` may name its timing column (`"timing": "TK:1"`) where detection picks a
  wrong one: the flic carriers print `FLIC_FRAME`, whole and never falling, before `TICKS`. The
  local flic carriers carry these hints; with them the flic probes match 126 more lines and replay to
  their end. `--timing` on the command line still wins. A `"startup_step"` (ticks) names the first
  sampled cycle's step where the samples cannot show it. A trace sampled only during a flic has
  1.094-tick gaps, though its start-up ran 60 Hz frames; the 18-cycle rule accepts only such frames
  (0.25 to 0.3125). With these, the flic replays no longer start one flic step late. A
  `"time_corr"` asks for fixed frames, for a carrier whose lines print no `TICKS`.
  `--time-corr x` runs fixed frames instead.
  The replay clock since 2026-11-03 (all harness-only; `RANDOM` aside, no engine behaviour changed):
  - **Frame cadence.** DOSBox-X's frames last whole 1/32 ticks and their running total is
    `floor(phase + k·rate)` units: at about 61 fps 8s and 9s spread as evenly as the rate allows,
    never three 8s in a row. `OracleTrace.FitCadence` finds the (rate, phase) that reproduces the
    longest run of steps of two neighbouring sizes (at most 256), and after the last sample the
    replay continues that cadence instead of the run's median step (the median of the last five
    steps where none fits). The probes' `WAITT 2` tails then resume when the original's did: the
    extra `TK` line after `PS`/`S`/`QS` is gone from all eight view-activity carriers that had it, and dist-out,
    sprite-occlude, long-pad, long-wall, range-d1, tilt-far (50/51-unit steps) and
    scheduler-order startup match in full.
  - **Save-anchored start-ups.** A carrier with no timing column but an original full save
    (`OVSAV*.SAV` in `work/`) takes the earliest save's `TOTAL_FRAMES` and `TICKS` as its clock: that
    many frames summing to those ticks in whole 1/32 units. The save's own `TIME_CORR` is a 60 Hz
    frame's in the save-body carriers, so the last frames run the 61 fps cadence, phased to end on
    that frame, and the first frame takes the surplus (44 frames to 13.875: a 78-unit first frame,
    then 8s and 9s); a run with no surplus is spread evenly, phased the same way. The header line
    says `save-anchored …`. The replay had run 56 quarter-tick frames to 14.0 there. With the walls'
    heap pointers at +28..+39 of each wall record set aside (the same far pointers in every launch),
    85 of the 107 save-body carriers match in full, from none.
  - **Other tokens' lines.** A named sampler's other tokens still measure the clock where they
    print the count and `TICKS`: the probe action's line after a long `WAITT`, say, long after the
    sampler stopped (scheduler-order round2 start-wait's `W16` 49 cycles after its last `Y`), or
    an event line between sparse samples. Such a line counts the cycle or the one before (printed
    after or before the sampler), so it is placed in whichever of the two the sampler-only fill,
    continued past its end at its own cadence, predicts nearer, when its value keeps the samples'
    order and its step from the sampled cycle before is a cycle's; the fill is then done again with
    it, when the value lies within two typical steps of the prediction (a line printed after the
    probe switched the counter off carries a stale count and lies cycles past it). Ten more
    carriers match in full (start-wait, turn-refresh, climb-1, climb-2, clip-wall, solid-dist0,
    clip-regions-order, clip-regions-walk, audio reuse, flic-scheduler-detail round2 near-control)
    and 880 more lines over all. `--dump-clock` prints the sampled cycles the replay's clock follows.
  - **Counted gaps.** A counted sampler's gaps are filled along one cadence through every sample and
    through the step into it that the sampler's `TIME_CORR` column prints (a column of whole 1/32
    ticks between 0.1 and 2 that equals the step wherever two samples are a cycle apart), for as
    many consecutive 60 Hz gaps as one cadence fits. The old fill restarted at phase 0 in every gap,
    which no constant-rate run produces, and always made the step into a sample 9/32. That column
    also gives the first cycle's step when no other rule does. waits-control (27 samples 123 cycles
    apart, one cadence through all), scheduler-order round5 waitt-small, wall-pass-clip
    clip-objects and audio round2 stop-lag match in full.
  - **Across a flic's end.** A gap whose first sample prints a flic step (`TIME_CORR` 1.094 or
    0.625, not a 60 Hz frame's) keeps that step for as many cycles as leave the world cycles after
    it at least 8/32 each (the flic's remaining frames: 10 in waits, 44 in round3 speed, 14 in
    replace's second flic). The world cycles follow the cadence of the samples after the flic, or
    whole 1/32 steps back from the next sample ending on its printed `TIME_CORR`, the first cycle
    taking the off-grid rest. flic-scheduler-detail waits, round3 speed and replace match in full.
  - **8-tick launches.** Most launches from 2026-10-15 to 2026-10-21 ran at 8 ticks a frame. When
    every positive first-column value of the trace (and the `TCK=` stamp) is a whole multiple of 8,
    the replay runs fixed 8-tick frames (`OracleTrace.RanAtSlowFrames`) instead of guessing frames
    from samples that often come one per several cycles; the each-sec-cadence carriers' `SECS`
    column no longer passes for a cycle counter. each-sec-cadence cadence, phase, slot-16 and
    wait-inside, clip-contact clip-regions and clip-detail wall-regions, round2 wall-dist-clip and
    round3 wall-regions-visible match in full (the last two had a stalled `pe_n` that sent the
    counter detection to another object's run count).
  - **Start-up.** The 18-cycle rule for the first step applies only when the first sampled step
    is a 60 Hz frame's: view-activity near-low and occlude-edge start at 8-tick frames (24, 32)
    before they speed up, and their start offset of −10.656 is gone (near-low matches in full).
  - **`RANDOM`.** The replay opens the archive with a fixed `RANDOM` seed (1), kept across level
    changes, so a carrier that draws `RANDOM` replays the same way every time. Before, motion-timing
    round2 anim-random matched a different number of lines on each run of one build (12, 23 and
    263 of 636 were seen); with the seed it matches 72. The original's generator is not modelled.
  Over all carriers (against the 2026-11-03 merged baseline): 307 → 330 full carriers of those
  run, 2242 → 2276 matching files, 26042 → 26231 matching lines (26042 − 263 → 26231 − 72 without
  anim-random). Four files match fewer lines, none on the evidence: anim-random (random); clip-contact
  rerun clip-regions-order (its first `TK` line, 31 ticks before the first `T` sample, where the
  start-up is unsampled); proximity-events near-walk (`TK` every 62 cycles with no `TIME_CORR`; one
  cadence fits all 40 samples with the phase within 0.06, and the replay's position drifts from the
  original's by 0.006 to 0.018 a sample either way, so the old line-4 match was 0.002 inside the
  tolerance); wall-pass-clip pass-dist6's `OVEND0.TXT`, where both replays carry a start offset
  (−1.375 then −1.531) and the later one prints its last `W` after `END`.
  With `OV_REPLAY_ROOT` set, each carrier's replay goes
  under that folder (at the carrier's own path) instead of `<carrier>/ov-replay/`, so several
  checkouts can diff the same carriers at once. Carriers with scripted input (`AUTOTYPE`,
  `x11_click.py`) are flagged: the replay presses nothing.
- `tools/oracle/measure_shoot.py` — reduces the shoot-mapping outputs to one CSV row
  per variant: the marker letter, the raw `HIN_*`/`HOUT_*` values, the nearest model for
  the lateral mapping, the vertical sign, the shear and `HIT_DIST`, and (from the
  screenshot) which bitmap half is drawn on the screen left of each sprite and of the
  far-wall face.
- `tools/oracle/measure_projection.py` — deterministic measurement of the
  projection probe screenshots (own PCX decoder; numpy required, Pillow only for
  non-PCX captures). Prints one CSV row per variant with raw pixel measurements
  and a `status`/`flags` pair that says when an estimator was withheld.
  `--selftest` re-renders the rooms with a pinhole model (shear, pitch, seams,
  blobs) and checks the estimators.
