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
  and runs it headless through `RuntimeHost` until the probe's `EXIT` or 64 ticks past the
  original's last sample; the replay's `PRINTFILE` output lands in
  `<carrier>/ov-replay/openvirtue-prints/TITLE/`. `diff` does the same and compares every `OV*.TXT`
  the original wrote (except the autoexec's `OVDIR.TXT`/`OVCON.TXT`) with the replay's, line by line:
  same token, same field count, numbers within `--tolerance` (default 0.002). Given a probe folder it
  walks every carrier below it (round sub-folders included) and skips carriers that never ran.
  Timing: the original's `TICKS` per cycle come from one token's column (`--timing TOKEN:COL`,
  detected by default: the first non-negative, never-decreasing column of the densest token; a
  whole-number cycle counter on the same lines fills in samplers that print only every few cycles,
  in whole 1/32 ticks; a trace where no token repeats but every line prints `TICKS` and one cycle
  counter, each counted cycle with one `TICKS`, is its own sampler, `*:0`).
  Until the replay prints its first sampled cycle every frame lasts the original's first sampled
  step. That step, the first sampled cycle's `TIME_CORR`, is read from a column that prints it,
  or else from the step 18 cycles later: DOSBox-X's 60 Hz steps repeat every 18 cycles. This
  moved the first difference of the bullet carrier from line 3 to line 284, and added 668 matching
  lines over all carriers; from then on each frame lands `TICKS` on the original's next sample, shifted by the start
  offset, which is reported, and columns that print `TICKS` are compared relative to it. A carrier
  without a per-cycle timing column is anchored on the template's start-up stamp (`TCK=` in
  `OVPRB0.TXT`, printed by carriers prepared from 2026-10-29 on) or else on the trace's first line;
  `KEY=value` lines compare their numbers, the stamp within one start-up cycle.
  Before the first sample the replay plans its start-up in whole 1/32 ticks, so it reaches the
  original's first sample at the original's `TICKS`. A carrier's `AUTOTYPE` keys are typed into the
  first `INKEY` it opens; clicks and key hooks are still not replayed.
  `--time-corr x` runs fixed frames instead. Carriers with scripted input (`AUTOTYPE`,
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
