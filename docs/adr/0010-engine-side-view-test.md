# ADR-0010: The engine decides what is in view; the renderer does not report it
Date: 2026-10-20
Status: accepted

## Context

Some Acknex-3 behaviour depends on what the player sees. `SKIP_FRAMES` (manual p.113) moves an
actor that is not visible and outside its `DIST` only every (`SKIP_FRAMES` + 1)th cycle
(OV-20261019-border-kinds), a `BERKELEY` wall runs its `EACH_TICK` only while it is in view
(OV-20261019-clip-detail), and the `#VISIBLE` flag of walls and objects (p.53, p.70) can be read
by scripts. OV-20261020-view-activity measured what "in view" means in the original: see that
note for the exact rule.

`OpenVirtue.Engine` has no graphics dependency, and `WdlRuntime` runs headless in every engine
test. The GL renderer in `OpenVirtue.App` draws the frame after `WdlRuntime.Tick` and the player
move (`RuntimeHost.Frame`), so the original's "judged from the last drawn frame" is the state at
the start of the next `Tick`.

## Decision

`WdlRuntime` computes the view itself, at the start of every `Tick`, from the player's position,
eye height and `PLAYER_ANGLE`, the `PLAYER_ARC` cone and the tilted vertical window of
`ProjectionModel`, and the level's wall geometry (a sight-line walk past walls and region
openings, like the hitscan's). The model lives in `OpenVirtue.Engine.Rendering.ViewTest`, which has
no graphics dependency. `WdlRuntime` sets `VISIBLE` on things, actors and `BERKELEY` walls and keeps
the result for the cycle's `SKIP_FRAMES` and `BERKELEY` gates. Nothing flows back from the
renderer.

## Alternatives considered

- **Visibility reported by the renderer through `RuntimeHost`** (e.g. GL occlusion queries or a
  list of drawn objects). Rejected: engine tests would need a fake renderer to see anything, the
  answer would depend on the GPU path and frame pacing, and a headless host (tests, tools,
  benchmarks) would see nothing at all. The host would also have to feed the result back into the
  interpreter between frames, coupling `IRenderBackend` to scheduler semantics.
- **No view test (treat everything as visible).** Rejected: actors behind the player would move
  every cycle where the original jumps them, and `BERKELEY` walls would never sleep.

## Consequences

- The view test is a model of the original's renderer, not of ours. If our renderer culls or
  draws differently, gameplay still follows the measured rule.
- A level without a registered player has no view: everything counts as visible and nothing is
  skipped, which keeps the existing headless tests unchanged.
- The test costs a few sight lines per thing, actor and `BERKELEY` wall per cycle. Each is checked
  only against the walls of the 16-step grid cells it crosses: under 1 ms a frame on the retail
  levels (about 7000 walls, 600–1000 things and actors). The grid is built once per level (5–8 ms)
  and follows moving vertices. Other walls get no `VISIBLE` flag until a need for it exists.
