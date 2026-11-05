# OV-20261105 — The ambient a wall's piece takes

Status: confirmed (one round, seven carriers) and implemented; "faces" below is the region
definition of OV-20261105-wall-pieces (round 6), which limits each piece to that region's
surfaces.

## Question
A wall between two regions stands between two `AMBIENT`s. OV-20261029-shading read one such wall as
taking its left region's ambient and OpenVirtue has multiplied a wall's texels by that region's
ambient since. It came up on Apathy's start frame: the left alcove's bricks came out about four
shades too bright (OV-20261104-retail-frames), a wall whose left region is not the side the eye
sees. Which region's `AMBIENT` does a wall's texel take: the left region's, the eye's, or the
region on the side the piece faces?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963 (`VRUN.EXE -NJ TITLE -RUN`).
- Fixture generator: `ovtool oracle prepare wall-light <dir>`
  (`tools/OpenVirtue.Tools/WallLightProbeCases.cs`). The wall-pieces fixture with two ambients:
  the room at x −16…48, y −24…24 (floor 0, ceiling 16) and the pad island x −12…−4, y 12…20,
  whose floor and ceiling each carrier moves. `PLAYER_LIGHT` 0 and the palette's 32-step gray
  ramp at indices 16–47 declared `RANGE 16, 32`, so a texel's palette index is 16 plus its shade
  level: `AMBIENT` 0.5 gives 30, `AMBIENT` 0.25 gives 38.
- Views: the FAR view (eye (24, −8) at z 8, `PLAYER_ANGLE` 2.498, tilt 0) sees the pad's step
  from the room; the NEAR view (the origin, `PLAYER_ANGLE` 2.03, `PLAYER_TILT` 0.3) looks down
  into the sunk pad from the room. Vertical focal 659 px, horizontal 585.5 px (OV-20261009).
- The index over each piece's face is read off `work/OVWLS0.PCX`; the piece's z range is computed
  back through the projection from the screen column named.
- Relevant local-only file hashes (sha256, first 8): `step-a` `45ad8f36`, `step-b` `c9bd8066`,
  `ceil-a` `453f90a0`, `ceil-b` `62845601`, `pit-a` `a35e96f8`, `pit-b` `e4ce9c42`, `both-a`
  `3cf27bd1`.
- Confirmation that no proprietary code/assets are committed: synthetic level and bitmaps only;
  nothing under `_research/` is committed.

## Readings
| Carrier | Room / pad `AMBIENT` | Wall read | Left region says | Eye's region says |
|---|---|---|---|---|
| step-a | 0.5 / 0.25, pad floor 6, the step face z 0…6 | 30 (level 14) on the face; 38 on the pad's floor edge above it | 38 | 30 |
| step-b | 0.25 / 0.5, the same step | 38 on the face | 30 | 38 |
| ceil-a | 0.5 / 0.25, pad ceiling 11, the face z 11…16 | 30 | 38 | 30 |
| ceil-b | 0.25 / 0.5, the same face | 38 | 30 | 38 |
| pit-a | 0.5 / 0.25, pad floor −4, the step face z −4…0 seen from the room | 38 on the face; 30 on the room's floor beyond it | 38 | 30 |
| pit-b | 0.25 / 0.5, the same face | 30 | 30 | 38 |
| both-a | 0.5 / 0.25, pad floor 4 and ceiling 12, pieces z 0…4 and z 12…16 | 30 on both pieces; 38 on the pad's floor and ceiling seen between them | 38 | 30 |

No single carrier tells all three candidates apart: step-a separates the left region's 38 from the
30 the facing region and the eye's region share, and pit-a separates the eye's region's 30 from the
38 the left region and the pit share. Together they fix the facing region. Two of them also show
that the two pieces are lit separately: in step-a the pad's floor above the step reads 38 (the
pad's own ambient) while the step face below it reads 30, and in both-a the pad's floor and
ceiling seen through the opening read 38 between the two 30 pieces.

## Rules
- **Each piece takes the `AMBIENT` of the region on the side it faces: the lower piece the lower
  floor's region F, the upper piece the higher ceiling's region C. Not the left region, and not
  the eye's region.** A step up to a pad is lit as the room; a dropped pad ceiling as the room; a
  pit seen from the room as the pit. On a tie the left region keeps it (unmeasured). F and C are
  the regions OV-20261105-wall-pieces limits each piece to, so a piece and the surface that cuts
  it short are the same region's: a room 0…16 against a solid 41…41 shows the room's face 0…16,
  lit as the room, and no second piece.
- **The earlier inference in OV-20261029-shading came from walls whose left region was also the
  facing one.** Round 3 there read a wall between a room and its solid border as the room's, which
  it is, as the room's floor is the lower one.
- **`GENIUS` is unchanged**: the lamp's gain still comes from the wall's own texture and the
  region that names the lamp, one value a wall (OV-20261104-genius).
- **A stacked (`BELOW`) wall's pieces keep the left region's ambient**: unmeasured, no carrier
  stood beside a stack.

## Open Questions
- A wall's ambient beside a `BELOW` stack, and where the two columns of a stack disagree.
- A piece's ambient where a piece is cut short by the region it faces and that region is a solid
  (a `border`): the piece is the facing region's, which is what the carriers' room-against-solid
  case shows, but no wall-light carrier used a solid above the room.
- A tie between the two regions' floors (or ceilings), which should keep the left region's: the
  carriers always separated them.
- A sloped floor or ceiling, where the two ends of a piece give different heights: the rule is
  written on the wall's ends, unmeasured.
- Whether the texture's own `AMBIENT` adds to the facing region's here as it does on floors
  (OV-20261029-shading round 3); all carriers had texture `AMBIENT` 0.

## Implementation
`MeshBuilder.WallPieces` gives each piece its ambient through `LowerFloorRegion` and
`HigherCeilingRegion` (a tie is the left region's), `WallAmbient` taking the piece's region — the
same region that limits the piece's extent (OV-20261105-wall-pieces round 6);
`StackedPiece` keeps `WallAmbient(level, wall)` for `BELOW` stacks. Tests: `MeshBuilderTests`
(`BuildWalls_WallToASolidRegion_TakesTheAmbientOfThePieceItFaces`,
`BuildWalls_SolidRegionOnTheLeft_ShowsTheRoomsFaceToItsCeiling`,
`BuildWalls_StepUpToAPad_TakesTheLowerFloorRegionsAmbient`,
`BuildWalls_DroppedPadCeiling_TakesTheHigherCeilingRegionsAmbient`,
`BuildWalls_Pit_TakesTheLowerFloorRegionsAmbient`,
`BuildWalls_AttachTakesTheAmbientOfThePieceItLiesOn`).
