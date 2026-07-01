# Near-Term Roadmap

This is the current practical plan after the first runtime/player integration.
It complements the larger milestone table in
[`docs/recon/06-reimplementation-strategy.md`](../recon/06-reimplementation-strategy.md).

## Track 1: Finish The Easy Part

"Easy" means work we can do from our own code, public docs, user-supplied data
files, guarded tests, and already-observed behavior without needing deep hidden
engine invariants.

1. Stabilize the viewer as a development harness.
   - Show live debug state: position, region, floor/ceiling, FPS, `TIME_CORR`,
     startup action status, and loaded world.
   - Keep current movement labeled as debug walking until parity work begins.
   - Make all six retail archives load through `ovtool level info` and the app
     where possible. Guarded local tests now cover all six archives through level
     load, mesh generation, texture decode, and runtime boot/tick; interactive app
     smoke remains manual.
2. Finish low-risk renderer/runtime foundation.
   - Sky/background handling.
   - First-pass palette/lighting approximation.
   - Sprite visibility/sorting fixes as needed for static scenes.
   - Runtime object registration for globals, placed things/actors, and player.
   - `each_cycle` scheduler skeleton with tests before exact parity tuning.
3. Convert assumptions into tests.
   - Prefer synthetic WDL/WMP fixtures.
   - Keep real-data tests guarded and local-only.
   - Run the CI-shaped build/test pair before merging.

## Track 2: Discover Hidden Invariants Cleanly

Once the easy harness/foundation work is stable, use
[`docs/clean-room/oracle-protocol.md`](../clean-room/oracle-protocol.md) for
behavior that only the old runtime can answer.

First targets:

1. WDL numeric edge cases.
2. Scheduler and action dispatch order.
3. Player movement units, angle conventions, jump/gravity constants, and portal
   collision thresholds.
4. Actor animation timing and AI hook triggers.
5. Palette lighting/shading behavior.
6. Save/load semantics, if retail-save compatibility remains a goal.

## Definition Of Done For This Phase

- `main` stays warning-clean under `dotnet build -warnaserror`.
- The app is a dependable level inspection harness, not just a renderer demo.
- Every newly discovered hidden invariant has an observation note and a test.
- The clean-room boundary is visible in docs, tests, and provenance notes.
