# OV-20261009-projection-fov-player-arc: Horizontal field of view as a function of PLAYER_ARC
Date: 2026-10-09
Status: proposed
Observer: TBD
Implementer: TBD

## Question
What horizontal field of view does the original renderer use for a given `PLAYER_ARC`, and
what is the functional form (focal length in pixels vs. `PLAYER_ARC`, at `ASPECT = 1`)?

## Why It Matters
The current app (`Camera.Projection`) passes `PLAYER_ARC` straight into
`CreatePerspectiveFieldOfView` as a **vertical** FOV **in radians** (1.0 → 57.3° vertical, so
~85° horizontal at 16:10). The 1998 manual (p.111) defines `PLAYER_ARC` as the eye's "focal
length range … (0.2..2.0)" where the default 1.0 is "approximately the field of vision of the
human eye (ca. 60°)" — a horizontal field, and not literally radians. The hitscan projection
(`SHOOT_X/SHOOT_Y`) shares the same constants. This is the most likely cause of the
"proportions out of whack" seen in the last Windows demo, and the GL renderer (ADR-0008)
must not inherit it. Parity of every sprite size, wall height and distance impression hangs on it.

## Clean-Room Boundary
- Oracle used: black-box DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), or the
  Skaphander 1995 runtime as a cross-check.
- Reference material consulted: 1998 A3 manual p.102 (`SCREEN_*`, `ASPECT`), p.111 (`PLAYER_ARC`,
  `PLAYER_TILT`); no source-level material.
- Split-role mode used: not needed (black-box geometric measurement).
- Local-only artifacts: `_research/oracle-runs/OV-20261009-projection-fov-player-arc/`.
- Confirmation that no proprietary code/assets are committed: screenshots of a synthetic
  level only; retail screenshots stay local.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963 under DOSBox-X, `VIDEO 320x200`,
  `aspect=false` in DOSBox-X so screenshots are in raw framebuffer pixels.
- Synthetic level (`ovtool oracle prepare projection-fov`, to be written): one rectangular
  region, player at a known distance D from a flat wall of known width W, looking straight at
  it, `PLAYER_TILT = 0`, `SCREEN_WIDTH = 320`, `SCREEN_HGT = 200`.
- Scenario: screenshot at `PLAYER_ARC` = 0.5, 1.0, 1.5, 2.0 (set in `IF_START`); measure the
  wall's pixel span S. Focal length in pixels f = S · D / W; horizontal FOV = 2·atan(160 / f).
- Repeat at `VIDEO X320x240` once to see whether f scales with width only.

## Observations
| Input / State | Observed Output / State | Notes |
|---------------|-------------------------|-------|
| `PLAYER_ARC` 0.5 → S px | | |
| `PLAYER_ARC` 1.0 → S px | | expect ≈ 60° horizontal per manual |
| `PLAYER_ARC` 1.5 → S px | | |
| `PLAYER_ARC` 2.0 → S px | | |

## Conclusion
TBD. Candidate forms to test against the data: f = (W_screen/2) / tan(arc · 30°);
f = (W_screen/2) / arc; f ∝ 1/tan(arc/2).

## Confidence
—

## Open Questions
- Does `ASPECT` (p.102) alter the vertical scale only, as the manual implies?
- Is the vertical pixel scale derived from f alone (pure 2.5D) or adjusted for 320x200's
  1.2 pixel aspect? See OV-20261009-pixel-aspect-video-modes.

## Tests To Add Or Update
- Replace the `PLAYER_ARC`-as-vertical-radians projection with the measured form; add a
  synthetic test projecting a known wall and asserting its screen span for arc 0.5/1.0/2.0.
- Re-derive the hitscan `SHOOT_X/SHOOT_Y` deviation from the same focal length.
