# OV-20261030-wall-sides: which side of a two- or four-sided wall texture is drawn
Date: 2026-10-30
Status: confirmed (one round, 11 screenshots) and implemented
Observer: OpenVirtue maintainers (black-box probe, screenshots)
Implementer: OpenVirtue maintainers

## Question
The manual (p.37) says a two-sided wall texture shows its first side "from the first vertex of the
wall up to the middle" and its second "from the middle to the end vertex", "depending on the
perspective the wall is perceived from". A four-sided one gives the first two to the wall's "right
side or front" and the last two to its "left side or back". Is the split along the wall itself, or
by where the eye stands? And which face is the front?

## Why It Matters
Both retail levels use four-sided wall textures for real effects. LEGALISM's `oneWayWall` shows a
maze wall from one side and a blank bitmap from the other. Apathy's `galCarpetStairs` shows floor
from one side and ceiling from the other. OpenVirtue drew the first bitmap from both sides.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963); screenshots only.
- Reference material consulted: the 1998 A3 manual p.37 (`SIDES`).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261030-wall-sides/`.
- Relevant local-only file hashes (sha256, first 8), `OVWSS0.PCX`: s4-west `2cdd78ad`, s4-east
  `b07d78da`, s4-west-v1 `b1a3efda`, s4-west-v2 `ad4c6edf`, s4-east-v1 `1d5992c9`, s4-east-v2
  `e8cc6b5a`, s2-west `2cdd78ad`, s2-east `c6b2303f`, s2-west-v1 `b1a3efda`, s2-west-v2 `ad4c6edf`,
  s2-east-v1 `934b4656`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
- Carriers: `ovtool oracle prepare wall-sides` (`tools/OpenVirtue.Tools/WallSidesProbeCases.cs`),
  run with `SDL_VIDEODRIVER=dummy`.
- A free-standing wall runs from v1 (24, −6) to v2 (24, 6) in the probe room. Its texture's bitmaps
  are the probe's solid colours: four sides red, green, yellow, blue; two sides red, green.
- Each case puts the eye 16 from the wall's line, west (x 8) or east (x 40), at y 0, −12 or 12. It
  faces the wall's middle, and the scaffold's screenshot is read along its middle row.

## Observations
| Eye | Four sides | Two sides |
|---|---|---|
| west, y 0 | green (2) | green (2) |
| west, y −12 (towards v1) | red (1) | red (1) |
| west, y 12 (towards v2) | green (2) | green (2) |
| east, y 0 | blue (4) | green (2) |
| east, y −12 | yellow (3) | red (1) |
| east, y 12 | blue (4) | — |

In every screenshot the wall shows one colour across its whole width.

## Invariant
- The side depends on where the eye stands, not on which part of the wall is seen.
- With four sides (or more), sides 1–2 are seen from the face to the left of v1 → v2: the cross
  product of v1 → v2 and v1 → eye is positive. That is the west face here, for a wall running +y.
  Sides 3–4 are seen from the other face.
- Within a pair, the first side is seen while the eye, projected on the wall's line, lies before
  the middle. From the middle on (here, square on) the second side is seen.
- A two-sided texture uses the same rule from both faces.

## Implementation
- `TextureAnimation.WallSide` gives the side.
- `MeshBuilder` gives a wall with a sided texture its own batch, with the wall as its source.
- `RuntimeHost.AddWorldBatches` picks the side each frame from the camera's map position.
- Tested by `MeshBuilderTests.BuildWalls_SidedTexture_ShowsTheSideTheViewpointPicks`, with the
  11 views above.

## Open Questions
- Whether the back face mirrors the bitmap's u direction (solid colours cannot show it).
- The lower storeys of a `BELOW` stack still take side 4·storey without the viewpoint rule.
