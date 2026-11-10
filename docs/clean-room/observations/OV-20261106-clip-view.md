# OV-20261106-clip-view: What CLIP_DIST and a wall's column do to the picture
Date: 2026-11-06
Status: confirmed and implemented
Observer: Claude Code (Opus 5.5)
Implementer: same

## Question
Retail regions set `CLIP_DIST` and the level starts of Legalism and New Age showed sprites in
OpenVirtue the original does not: dark ovals in New Age's sky, a brown wedge down a Legalism corridor.
`CLIP_DIST` gated only the scheduler. Manual p.30: only walls and objects entirely within the distance
are rendered, walls outside drawn in palette colour 1; p.105: `CLIPPING` 0, 0.5 and 1; p.55: `FAR`. Which
distance, what becomes of a floor, a ceiling, a sky and a sprite, and what hides what in a column?

## Why It Matters
Whatever stands behind a wall at a greater height showed through in OpenVirtue, on every level. A
measure of the whole corpus found regions that lost their floors and ceilings entirely as well.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Reference material consulted: the 1998 manual p.30, p.55, p.105, region `CLIP_DIST` (ch. 8).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261106-clip-view/` and `round2`–`round6`.
- Relevant local-only file hashes (sha256, first 8), `OVCVS0.PCX`: r60 `7630a7f3`, r50 `ee73cc1c`, r40,
  n-eq, s-31-c30 and q-vis `0f58c184`, skill-decl `06ab9477`, far-0 `0952cb58`, n-one `8c779f17`, t-20
  `528a9e38`, island `a865b1b3`, island-tiny `327837b9`, s-29-c30 `42711e7d`, s-40-t00 `f779c78c`, h-wall
  `bd017c0a`, h-room `ca79be06`, p-wide `2a59e4a4`, p-behind `dddb2272`, q-near-color `107d3995`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only; the retail
  sentences above say what a frame shows and name no data.

## Method
`ovtool oracle prepare clip-view` (`tools/OpenVirtue.Tools/ClipViewProbeCases.cs`). The template room:
far wall of stripes at x 48, side walls from x −16 to 48, half width 24, eye at the origin looking along
+x. Palette index 1 is the clip colour, 11 a thing's, 10 an actor's. Each screenshot is read along row
240 and column 320.

## Observations
| Case | Reading |
|---|---|
| control, r100 | whole room |
| r60 | the stripes y 12…24 and −24…−12 are index 1 in the **whole column**, floor and ceiling too (140826 pixels, 294 columns × 479 rows); stripe y 8…12 (56 and 60 by axes) stays, y 12…16 (60 and 64) goes: `\|dx\| + \|dy\|` to the nearer vertex, below `CLIP_DIST` |
| r50, r40 | the far wall goes at 50; the side walls (nearer vertex 40) go at 40 |
| n-eq | a wall exactly 25 away with `CLIP_DIST` 25: out |
| n-one, n-one-05, n-one-1 | one vertex in and one out: drawn at every `CLIPPING`; n-in, -05, -1 agree: `CLIPPING` changes nothing here |
| far-0, far-05, far-1 | `FAR`: drawn at every `CLIPPING`, with the floor and ceiling before it |
| skill-decl, keyword, set-live, set-region | a `SKILL CLIP_DIST`, a top-level `CLIP_DIST` and `SET CLIP_DIST` do nothing: only the region's value counts, and `PRINT_VALUE CLIP_DIST` is a compile error |
| t-20, s-29-c30 | every wall out: the thing at 20 or 29 draws over the clip colour |
| s-31-c30, s-40-c30 | at 31 or 40 it does not |
| s-40-t50, -t25, -t00, s-40-a25, a-31 | `THING_DIST` and `ACTOR_DIST` change nothing on the picture |
| o-pad-tiny | a thing in a pad with `CLIP_DIST` 5, 24 away: not drawn |
| o-pad-big | the pad unset, the room's 20: not drawn: the smaller of its own region's and the player's |
| island | a raised pad (floor 4, ceiling 12) with the room's walls behind it out: the near piece, the pad's floor and ceiling, then index 1 in the far opening only |
| island-big | the pad's own 1000: the same: the room's walls are seen from the room |
| island-tiny | the pad's own 5: its far wall out: index 1 between the near pieces, the pad's floor and ceiling gone |
| q-vis, q-invis, q-color, q-color-vis | a level pad, its walls visible or not, the room's 20: index 1 in all 479 rows: a level border closes nothing |
| q-near-color, pad-zero, pad-small | a level pad with a small `CLIP_DIST`: nothing changes: a level border is no stop for the walk, so the pad's value never applies |
| h-room, h-low | a thing above the ceiling or below the floor of its own region: drawn over them |
| h-wall, h-pad | a high thing behind a wall reaching the ceiling: **not drawn, even in the rows above the wall's top** |
| p-behind | behind a raised slab: gone where the slab's floor would cover it, and p-wide cuts a wide one at the slab's far top edge |
| p-wide | a 16-step-wide thing beside the room's walls: **all 362 columns drawn, over the walls of its own region** |

## Rules
- **A column is walked from the eye. A wall seen from a region is out of reach once `|dx| + |dy|` to its
  nearer vertex is not below that region's `CLIP_DIST`, unless it has `FAR`; a region without a value
  keeps 1000.** `CLIPPING`, a `CLIP_DIST` skill and a top-level `CLIP_DIST` do nothing on this runtime.
- **An out-of-reach wall ends the walk, and everything still open in the column reads palette entry 1**,
  the seeing region's floor and ceiling too. A step crossed on the way keeps the rows it closed.
- **A border between regions of one height is no stop at all**, flagged `INVISIBLE` or not.
- **A step's lower piece hides what is behind it all the way down, its upper piece all the way up, a wall
  of one region or one with no opening both ways.** So a high sprite behind a wall is hidden above the
  wall's top, and so is a far region's ceiling.
- **A sprite is drawn while `|dx| + |dy|` to it is below the smaller of its own region's and the player's
  region's `CLIP_DIST`.** `THING_DIST` and `ACTOR_DIST` do nothing on the picture.
- **A sprite is not cut by the walls bounding its own region**: a wide one beside a wall shows across it.
  A free wall in the region, or any wall on the way into it, still hides it.
- **A sprite seen from another region is cut at the opening of each border it is seen across**, at the
  border's floor and ceiling lines even where both sides are level (below-view room-high: the deck's thing
  ends at the deck's near ceiling line, and its part outside the deck's footprint is whole). A sprite in a
  lower storey of a `BELOW` stack, seen from outside it, shows within that storey only; and of a stack only
  the slabs between storeys hide sprites, not the top storey's ceiling or the bottom one's floor. With
  these, below-view goes from 91 004 differing pixels to 2 646 and below-beam from 32 000–108 000 to 400–650.
- **A closed region (floor at or above ceiling) shows no surface**, and an `INVISIBLE` wall no face.
- A region of no single wall loop (an island inside it, a vertex three walls meet at) has floors and
  ceilings like any other. OpenVirtue drew none for them: 158 of Apathy's 1073 regions, 81 of Heart's
  1429, 94 of Legalism's 1108, 185 of New Age's 1352.

## Confidence
High: each rule has several carriers, and the retail starts now show the far walls and the sky the
original shows, the ovals and the wedge gone. Medium for the sprite's hiding depth: measured at one
wide sprite, and OpenVirtue implements it by a depth test against its own walls' nearest depth.

## Open Questions
- The default 1000 in a level larger than it: no carrier reached it.
- An `INVISIBLE` step between two ceilings: whether its rows show the far ceiling's plane, as a floor
  step's do, is not measured.
- A sprite beside its own region's wall when a nearer wall of another region also crosses its columns.
- Fills beside a `BELOW` stack keep their old behaviour.
- A `SKILL CLIP_DIST` declaration changed nothing with the region unset, in V3.963 only. A V3.990 runtime
  may differ, as it does with `APPEND_MODE`.

## Implementation
`ViewClip` (`IsWallOutOfReach`, `AddFills`, `IsEntityInReach`, `SpriteHidingDepth`), `MeshBuilder.BuildOccluders`,
`LevelTopology.RegionEdges`, `RegionSpans` and `RegionContains`, `MeshBuilder.AddOutlineSurfaces`.
`RuntimeHost.DrawWorld` draws, in order: backdrop, sky, the occluders depth-only and pushed a hair back, the
world, the fills pulled a hair forward, the sprites, the top row. Tests: `ViewClipTests`.

## Addendum: an INVISIBLE step's rows (2026-11-10)
Read from the round 5 screenshots (`p-top`, `p-front`, `p-beside`, `p-behind`, `p-wide`): a raised pad
(floor 4) behind the carrier's invisible passable boundary walls.

- **An `INVISIBLE` step's rows show the higher floor's plane continued toward the eye.** Each riser pixel
  carries the texel, and the light, of where its line of sight meets the pad's floor height. In `p-top`
  that is the pad's grey down to the room's floor line, where OpenVirtue left the rows black.
- **Such a step hides nothing behind it.** At the pad's left corner the side wall is seen almost
  edge-on, and the original shows the far magenta wall in that column. OpenVirtue's lower-piece occluder
  for the invisible wall hid it there, with nothing drawn in its place.

Implemented in `MeshBuilder.AddInvisibleSteps`: a quad over the lower piece, in the higher floor's
surface bucket, flagged `RenderVertex.PlaneFill`. The shader takes the texel and depth from the plane.
`MeshBuilder.BuildOccluders` gives an `INVISIBLE` wall's lower piece no occluder; a sloped higher floor
and a `BELOW` stack are left as they were. Differing pixels: p-top 19 953 → 211 (the `p-none` control
reads 212), p-front 19 888 → 146, p-beside 20 019 → 277, p-behind 22 542 → 3 566, p-wide 38 442 →
23 851. wall-pieces' padded carriers also gain about 5 500 each. Over clip-view, wall-pieces,
border-kinds, border-climb, texmap, attach and blend (220 screens) the total goes 1 250 340 → 1 113 485,
13 screens better and none worse. Tests: `ViewClipTests.BuildOccluders_InvisibleStep_HidesNothing` and
`ViewClipTests.Build_InvisibleStep_ContinuesTheHigherFloorsPlaneOverItsRows`.
- **A sprite's own region's open borders are ways into it, not walls bounding it.** A thing in the room
  seen across a raised pad is cut at the pad's openings (`p-behind`), so the pad's borders no longer pull
  its hiding depth ahead. Only walls onto a closed region, and a `BELOW` stack's borders (below-view
  `deck`), still do. p-behind 3 566 → 146, p-wide 23 851 → 16 953, round 4 o-step-open 3 714 → 299. Over
  clip-view, below-view, below-beam, sprite-shade and view-activity, three screens are better and none
  worse. Still open in p-wide: the wide thing should be cut at the pad's far edge, yet drawn over the
  room's back wall. A single hiding depth per sprite cannot give both. Tests:
  `ViewClipTests.SpriteHidingDepth_BeyondAnOpenPad_KeepsItsDepth` and
  `ViewClipTests.SpriteHidingDepth_WideBesideItsRegionsWalls_ComesAheadOfThem`.
