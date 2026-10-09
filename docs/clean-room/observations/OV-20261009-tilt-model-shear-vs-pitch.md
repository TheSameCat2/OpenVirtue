# OV-20261009-tilt-model-shear-vs-pitch: Is PLAYER_TILT a y-shear or a true camera pitch?
Date: 2026-10-09
Status: proposed
Observer: TBD
Implementer: TBD

## Question
When `PLAYER_TILT` ≠ 0, do vertical wall edges stay vertical on screen (y-shearing: the
horizon line moves by `tan(tilt) · f` pixels) or converge (true perspective pitch)?

## Why It Matters
The app converts `PLAYER_TILT` (manual p.111: "tangent of the player's vertical line of
sight") into a camera pitch (`Atan(tilt)`) and rotates a 3-D view matrix. 2.5D sector engines
of this generation implement look-up/down as a y-shear, and the skill being defined as a
*tangent* is exactly the quantity a shear needs. If the original shears, our tilted frames
show walls leaning that the original never shows, sky/`SKY_OFFS_Y` behavior (p.37: "vertically
the sky texture is neither zoomed nor repeated") differs, and sprite/UI hit testing under tilt
diverges. The GL renderer must pick one model before it is written.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the patched DOS runtime.
- Reference material consulted: A3 manual p.37 (sky vs tilt), p.111 (`PLAYER_TILT`).
- Split-role mode used: not needed.
- Local-only artifacts: `_research/oracle-runs/OV-20261009-tilt-model-shear-vs-pitch/`.
- Confirmation that no proprietary code/assets are committed: synthetic level screenshots only.

## Setup
- Synthetic level: a tall wall with a high-contrast vertical stripe texture, player facing
  it at a known distance, `PLAYER_TILT` set to 0, 0.5, 1.0 in `IF_START` (three runs).
- Screenshot each; measure (a) whether the stripe edges are parallel (shear) or converge
  (pitch), (b) the horizon/eye-line displacement in pixels vs `tan(tilt) · f` using f from
  OV-20261009-projection-fov-player-arc.
- Also note the maximum usable tilt before the renderer clamps or artifacts appear.

## Observations
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| tilt 0 | | baseline eye line |
| tilt 0.5 | edges parallel? horizon shift px? | |
| tilt 1.0 | | |

## Conclusion
TBD.

## Confidence
—

## Open Questions
- How does the sky background move with tilt (pixel shift = horizon shift?).
- Does `SHOOT` under tilt use the same shear mapping for `SHOOT_Y`?

## Tests To Add Or Update
- If shear: replace camera pitch with a projection-matrix y-offset (shear term) and keep the
  view matrix yaw-only; synthetic test asserting vertical edges remain vertical under tilt.
- If pitch: keep the model, but calibrate the tangent→angle mapping against the measured shift.
