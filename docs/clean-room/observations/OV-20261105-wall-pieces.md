# OV-20261105 — A wall's pieces: PORTCULLIS, POSITION and ATTACH on a two-region wall; the row mask

Status: confirmed (six prepare rounds, 59 carriers) and implemented; round 6 limits each piece to
the region it faces.

## Question
Apathy's start frame, compared against the original (OV-20261104-retail-frames), showed two
green discs the original does not draw as discs and seat rows whose stripes ran the wrong way.
The first were the original's indicator `ATTACH` textures, missing in OpenVirtue because only a
one-region wall asked for its attach. The second came from two rules: `PORTCULLIS` anchors the
rows of *each piece* of a wall at that piece's own edge, and a wall bitmap's rows are read
through a bit mask, not a modulo of the bitmap's own height.

Round 6 asked what ends a piece when the region it faces is not the one whose surface bounds it: a
room's wall against retail's solid `border` (both floor and ceiling far above the room) seemed to
run on to the solid's own heights, and the anchors then hung off those instead of the room's.

So: where does a wall between two regions split, which heights do its two pieces span, where do
a `PORTCULLIS` wall's texture rows count from at `POSITION` 0 and 1 (on a flat and on a sloped
edge, and with `OFFSET_Y`), where does an `ATTACH` sit on such a wall (one per wall or one per
piece, from which height, cut how, and does `OFFSET_X`/`OFFSET_Y` slide it), and how are a wall
bitmap's rows and columns read when its height is not a power of two?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963 (`VRUN.EXE -NJ TITLE -RUN`).
- Fixture generator: `ovtool oracle prepare wall-pieces <dir>`
  (`tools/OpenVirtue.Tools/WallPiecesProbeCases.cs`). Synthetic level and bitmaps only: a room
  (floor 0, ceiling 16) closed by a solid border, a `pad` island at x −12…−4, y 12…20 whose
  floor and ceiling each carrier moves, and on the mask carriers a free wall inside it. The
  swatch is the level palette's 16×16 `PRBPAL.PCX` cut 16×12 (index = 16·row + column) or 12×12
  or 16×8, at `SCALE` 2 (and 4 for one carrier), so a screenshot pixel's palette index names the
  texel under it; the cyan square (index 10) is the attach, a 16×16 bitmap. Traces (`OVWPC0.TXT`).
- Round 6 (2026-11-05) reuses the same fixture with a solid-region family: the pad's floor and
  ceiling together at 41 and then at 43 (retail's rooms against a solid `border`), and a pad at
  19…24 and 19…25, each with `POSITION` 0 and 1 swatch carriers and three attach carriers. The
  carriers are local-only under `_research/oracle-runs/OV-20261105-wall-pieces/round6/` (`sol-p1`,
  `sol-p1-b`, `sol-p0`, `gap-p1`, `gap-p1-b`, `sol-att-a`, `sol-att-b`, `sol-att-c`) and their
  readings in `_research/oracle-runs/_session-20261105/round4_read.txt`. Two limits. The swatch
  carriers cannot separate an anchor at the room's ceiling 16 from the height-0 grid, 16 being a
  whole number of the swatch's 8-step period; the attach carriers do, since one step of attach at
  `POS_Y` 80 lands at 11–12 from 16 and at 36–37 from 41. And the band between the room's ceiling
  and the pad's floor is above the room's ceiling, so it cannot be observed from the room at all.
- Two views. FAR: eye (24, −8) at z 8, `PLAYER_ANGLE` 2.498, tilt 0, so the pad's south and east
  faces are whole on the screen. NEAR: the origin, `PLAYER_ANGLE` 2.03, `PLAYER_TILT` 0.3, looking
  down into a sunk pad. The projection is the measured one (OV-20261009): vertical focal 659 px,
  horizontal 585.5 px. The horizon fits 240.2–240.7 px at tilt 0 against the model's 240, and
  97.8 at tilt 0.3 against the shear model's 96.
- Palette indices are read straight off the screenshot `work/OVWPS0.PCX`; each piece's z range is
  computed back through the projection from the screen columns named in the readings.
- Relevant local-only file hashes (sha256, first 8): round 1 `att-cross` `d2053ed1`, `att-high`
  `3b56058f`, `plain-step` `4694522f`, `plain-ceil` `01ce0b17`, `port-both` `eaa2c346`,
  `port-ceil` `1e5ad494`, `port-free` `5fdf10ad`, `port-free-o0` `40ece732` (the same file as
  `port-freeoff`); round 2 `att-h60` `3d7a342b`, `mask-c12` `64a4fe9b`, `mask-nat12` `0248541e`,
  `mask-h8` `20b32b70`; round 4 `port-off3` `47eac927`, `port-slope` `29180749`; round 5
  `att-two-up` `b70a1e88`.
- Confirmation that no proprietary code/assets are committed: synthetic level and bitmaps only;
  nothing under `_research/` is committed. The one retail case below is quoted as an archive
  bitmap's size and the indices a frame draws, never as data.

## Readings

### Round 1: where the pieces are, and the rows a `PORTCULLIS` wall counts from
Twenty-four carriers. The swatch is 12 rows tall at 2 px a step, so `v & (H−1)` gives a period of
16 texel rows (8 steps), and the rows below are indices/16.

| Carrier | Reading |
|---|---|
| plain-step | pad floor 5, one lower piece z 0…5; no flag, rows 2, 3, 8, 9, 10, 11, 8, 9, 10, 11 down the face (indices 38, 54, 134, 150, 166, 182, 134, 150, 166, 182), row 11 on the piece's lower edge: the height-0 grid |
| port-step | pad floor 5, `PORTCULLIS`, `POSITION` 0: as plain-step, index for index: the piece's own lower edge is 0, where the height-0 grid already zeroes |
| port-step-p1 | `POSITION` 1: rows 0, 1, 2, 3, 0, 1, 2, 3, 8, 9 (indices 6, 22, 38, 54, 6, 22, 38, 54, 134, 150), row 0 on the piece's upper edge 5 |
| plain-ceil | pad ceiling dropped to 11, so one upper piece z 11…16; no flag: rows 0, 1, 2, 3, 0, 1, 2, 3, 8, 9 (indices 6, 22, 38, 54, 6, 22, 38, 54, 134, 150) from the height-0 grid |
| port-ceil | the same piece with `PORTCULLIS`, `POSITION` 0: rows 2, 3, 8, 9, 10, 11, 8, 9, 10, 11 (indices 38, 54, 134, 150, 166, 182, 134, 150, 166, 182), row 11 on the piece's own lower edge 11 |
| port-ceil-p1 | `POSITION` 1: as plain-ceil; the piece's top 16 is a whole 8-step period of the height-0 grid, so this carrier cannot separate the flag from the grid |
| port-both | pad floor 3, ceiling 11: pieces z 0…3 and 11…16; `POSITION` 0 reads the lower piece as plain-step and the upper as port-ceil, each from its own lower edge |
| port-both-p1 | `POSITION` 1: lower piece rows 0, 1, 2, 3, 0, 1, 2 from its upper edge 3 (indices 6, 22, 38, 54, 6, 22, 38), upper piece as port-ceil-p1 from 16 |
| port-pit | pad sunk to −4, piece z −4…0, `POSITION` 0: rows 8, 9, 10, 11, 8, 9, 10, 11 (indices 132, 148, 164, 180 twice), the height-0 grid would start at rows 0, 1, 2, 3 |
| port-pit-p1 | `POSITION` 1 from the piece's upper edge 0: rows 0, 1, 2, 3, 0, 1, 2, 3 (indices 4, 20, 36, 52 twice); 0 is also the height-0 zero, so it separates the piece's edge from the pit's declared floor −4, not `POSITION` 1 from the grid |
| port-free | a free 16-step wall at 8 px a step (an 8-step period), `POSITION` 1: rows 0…11 then 0…11 (indices 7, 23, 39, 55, 135, 151, 167, 183, repeating) |
| port-free-o0, port-freeoff | the same wall with `OFFSET_X` 4, `OFFSET_Y` 6 and `POSITION` 0 and 1: the two screenshots are the same file (`40ece732`), and the row just above the wall's lower edge reads 9 (index 155), not the plain 11: the offset counts. A 16-step wall is a whole number of the 8-step period, so neither carrier separates `POSITION`, and the three of them cannot separate the offsets from the grid either |
| att-upper | pad ceiling 8, upper piece z 8…16, plain wall, square at `POS_X` 8, `POS_Y` 16 on the blue `SCALE` 4 base: cyan z 11.02–12.02 and 11.04–11.96, three steps above the piece's foot 8: the attach's repeats come from the height-0 grid's top 16, not from the piece |
| att-open | `POS_Y` 48: no cyan; the square would sit at z 3–4, over the opening |
| att-cross | `POS_Y` 30: cyan z 8.01–8.46 and 8.01–8.41, half a square, cut at the piece's lower edge 8 |
| att-low | pad floor 4, ceiling 12: pieces 0…4 and 12…16, `POS_Y` 52: cyan z 1.92–2.92 and 1.99–2.91 on the lower piece (grid top 16) |
| att-high | the same pieces, `POS_Y` 4: cyan z 14.02–15.03 and 14.01–14.98 on the upper piece |
| att-grid-up | base at 8 px a step (an 8-step repeat), pad ceiling 12, `POS_Y` 8: no cyan; the height-0 grid puts the square at z 6–7 in the opening |
| att-grid-st | the same base, pad floor 9, `POS_Y` 8: cyan z 5.99–6.94 and 5.99–6.91 on the piece 0…9: the grid from height 0, not from the pad's floor 9 |
| att-grid-pit | the same base, pad sunk to −4, `POS_Y` 8: no cyan; the grid from height 0 puts it at z 6–7 over the opening |
| att-port-neg | `PORTCULLIS`, ceiling 11, `POS_Y` −4 at 4 px a step: no cyan. The piece's lower edge 11 plus one 16-step repeat puts the square at 27–28, so a step up does not bring it into 11…16 |
| att-port-pos | the same wall, `POS_Y` 4: no cyan |
| att-port-p1 | base at `SCALE_Y` 3 (a 21.33-step repeat), `POSITION` 1, ceiling 11, `POS_Y` 12: no cyan; the upper edge 16 plus that repeat is above the ceiling |

### Round 2: which edge of which piece, and the row mask
Seventeen new carriers.

| Carrier | Reading |
|---|---|
| att-p0-48 | pad ceiling 11 (one upper piece 11…16), `POSITION` 0, `POS_Y` 48 (12 steps): cyan z 14.00–14.97 and 14.01–15.02, i.e. 11 + 16 − 12, the piece's own lower edge plus one repeat |
| att-p1-48 | `POSITION` 1, `POS_Y` 48: no cyan; 16 + 16 − 12 is above the ceiling |
| att-p1-80 | `POSITION` 1, `POS_Y` 80 (20 steps): cyan z 11.00–12.00 and 11.02–11.94, 16 + 16 − 20 |
| att-p1-16 | `POSITION` 1, `POS_Y` 16: no cyan (28) |
| att-pstep | pad floor 5, one lower piece 0…5, `POSITION` 1, `POS_Y` 48: no cyan; 5 + 16 − 12 = 9 lies over the opening |
| att-pstep72 | `POSITION` 1, `POS_Y` 72: cyan z 1.93–2.93 and 2.00–2.91 (5 + 16 − 18) |
| att-ppit | pad sunk to −4, piece −4…0, `PORTCULLIS`, `POS_Y` 56: cyan z −3.07…−1.96 (−4 + 16 − 14) on the sunk step |
| att-pit-abs | the same with no flag and `POS_Y` 72: cyan z −3.07…−1.96 as well (0 + 16 − 18). The two carriers do not separate the flag here, but both hide a grid started at the pit's floor −4 |
| att-neg-up | base at 8 px a step (an 8-step repeat), pad ceiling 8, `POS_Y` −24: cyan z 10.01–10.93 and 10.02–10.96 (0 + 8 + 3) |
| att-neg-port | pad floor 13, `PORTCULLIS`, `POS_Y` −24: cyan z 9.99–10.94 and 9.99–10.90 (0 + 8 + 3) |
| att-free-9 | a free wall inside a pad at floor 9, `POS_Y` 8: no cyan; 0 + 8 − 1 = 7 is under the wall's foot 9 |
| att-free-neg | the same wall, `POS_Y` −24: cyan z 10.02–10.93 on the wall: the anchor is the fixed 0, not the region's floor 9 |
| att-h60 | a free wall carrying a 60-row cut of the blue bitmap at 4 px a step, `POS_Y` 4: cyan z 13.05–13.93, so one repeat is 15 steps (the bitmap's real height), not the masked period's 16 |
| mask-c12 | a 12×16 cut at 2 px a step: columns 0…11 then 0…3 (a modulo of 12, 32 of 32 runs matched); rows 0…15 twice, since 16 rows is already a power of two |
| mask-nat12 | a bitmap natively 12×12: columns as mask-c12; rows 0, 1, 2, 3, 0, 1, 2, 3, 8, 9, 10, 11, 8, 9, 10, 11: a 16-row period, `v & 11` |
| mask-h8 | the 16×8 control: rows 0…7 repeated (8 rows), columns 0…15 |
| mask-c12s4 | the 12×16 cut at 4 px a step with `OFFSET_X` 3: column 3 next to the second vertex, then 4, 5, … modulo 12 (64 of 64 runs); rows as mask-c12 |

### Round 4: the attach's offsets, and sloped edges
Eight new carriers.

| Carrier | Reading |
|---|---|
| att-off | a free wall at 8 px a step, `OFFSET_X` 8, `OFFSET_Y` 8, `POSITION`-free attach at `POS_X` 16, `POS_Y` 16: cyan z 9.02–9.94, 2.03–2.96 steps from the second vertex, i.e. `s0` = (16 − 8)/4 and top = 16 − (16 + 8)/4: both offsets slide the attach |
| att-off-p0 | the same with `PORTCULLIS`, `POSITION` 0 on a piece 0…16: the same square |
| att-two | pieces 0…3 and 11…16, `POSITION` 0, `POS_Y` 56: cyan z 0.92–1.93 and 0.96–1.93, on the lower piece only and once per face: one attach a wall, anchored at the lowest lower edge 0 |
| plain-off3 | a free wall in a pad at floor 3, `OFFSET_X` 4, `OFFSET_Y` 6, the 16×16 swatch at 2 px a step: rows from the height-0 grid with `OFFSET_Y` subtracted (806 of 813 rows), not from the region's floor 3 (0 of 813) |
| port-off3 | the same with `PORTCULLIS`, `POSITION` 0: rows from the piece's lower edge 3 with `OFFSET_Y` subtracted (806 of 813); the columns fit `c` = 2.90 + 1.98·d exactly as plain-off3's, so the flag leaves them alone |
| port-off3p1 | `POSITION` 1: rows from the upper edge 16 with `OFFSET_Y` subtracted (806 of 813) |
| port-slope | a sunk pad with `FLOOR_LIFTED` and corner heights 0, 2, 2, 0, so the far step's lower edge slopes from −2 at x −4 to −4 at x −12: the rows follow the sloped edge (329 of 379 sampled rows; the next best model, rows hung from the piece's lowest corner −4, fits 51) |
| port-slop-p1 | a raised pad, same corners, `POSITION` 1, the upper edge sloping from 3 to 5: the rows follow the sloped upper edge (274 of 274 rows on the sloped face, and 348 of 348 on the flat east face) |

### Round 5: one anchor for the whole wall

| Carrier | Reading |
|---|---|
| att-two-up | pieces 0…3 and 11…16, `POSITION` 0, `POS_Y` 4: cyan z 14.01–14.98 and 14.02–15.03 on the upper piece, so the anchor is the wall's lowest lower edge 0, not the upper piece's 11 (which would put it at 26) |
| att-two-p1 | `POSITION` 1, `POS_Y` 72: cyan z 13.03–13.95 and 13.02–14.02 on the upper piece only, from the highest upper edge 16 (16 + 16 − 18) |

### Round 6: a piece is limited by the region it faces
Eight carriers, the swatch 16×16 at `SCALE` 2 (an 8-step period) and the cyan square 16×16.

| Carrier | Reading |
|---|---|
| sol-p1 | pad floor and ceiling 41, `POSITION` 1: the wall shows 0…16 and nothing above it, and the row just below 16 is the bitmap's first — the room's ceiling 16, which the height-0 grid shares. A 41 origin would put row 2 there |
| sol-p1-b | the solid at 43, which separates the room's ceiling from the height-0 grid: row 0 just below 16 again; 43 would put row 6 |
| sol-p0 | `POSITION` 0: the plain height-0 grid from the wall's bottom 0 — row 15 just above z 0, row 0 at z 8 |
| gap-p1 | pad 19…24: from the room the wall shows 0…16, and the row just below 16 is row 0 (an origin at the pad's floor 19 would put row 6), so the lower piece ends at the room's ceiling; the upper piece 19…24 lies above that ceiling and cannot be seen from the room |
| gap-p1-b | the pad's ceiling at 25: row 0 just below 16 still, so the lower piece's top is the room's ceiling 16, not the upper piece's 25 (which would put row 2) |
| sol-att-a | pad 41…41, `POSITION` 1, the square at `POS_Y` 80 (20 steps): cyan z 10.93–11.93, i.e. 16 + 16 − 20. A 41 origin would hide it at 36–37 |
| sol-att-b | `POS_Y` 180 (45 steps): no cyan; 16 + 16 − 45 is under the floor, while a 41 origin would show the square at 11–12 |
| sol-att-c | `POSITION` 0, `POS_Y` 48 (12 steps): cyan z 2.84–3.85, 0 + 16 − 12 |

So a piece ends where a surface of the region it faces meets it, and both the texture rows and the
attach's origin follow those limited edges: against a solid 41…41 or 43, a room 0…16 shows the
room's face alone.

## Rules
- **A piece is limited by the region it faces: it is cut where a surface of that region meets it.**
  The lower piece, between the two floors, starts at the lower floor and ends at the lower of the
  higher floor and the facing region F's ceiling; the upper piece, between the two ceilings, ends
  at the higher ceiling and starts at the higher of the lower ceiling and the facing region C's
  floor. F is the lower-floor region and C the higher-ceiling region (a tie is the left region's);
  the limits are that region's own surface at each vertex. A wall of one region shows its whole
  floor to ceiling. So a room 0…16 against a solid 41…41 shows only 0…16 and no upper piece at
  all (round 6), a room 0…16 against a pad 19…24 shows 0…16 and 19…24, and **the two pieces never
  overlap**. A wall between two regions shows up to two pieces, a lower one between the two floors
  and an upper one between the two ceilings; a piece that would be empty (both regions' surfaces
  meeting all along it) is not drawn.
- **`PORTCULLIS`, `POSITION` 0: each piece counts its texture rows from its own lower edge, so the
  bitmap's last row sits on that edge. `POSITION` 1: from its own upper edge, the bitmap's first
  row on it.** `OFFSET_Y` counts on top with its usual sign (downward), and a sloped edge carries
  the rows along itself rather than levelling them, each end of a piece taking its own edge's
  height. Columns are unchanged by the flag. The manual (p.54) says the texture is fastened to the
  wall's lower or upper border and agrees in outline, but does not say "per piece": a two-piece
  wall has two anchors, one for each piece.
- **A wall's `ATTACH` is drawn on every wall, one square, one origin per wall, cut to each piece
  it crosses, never shown in the opening, and never repeated with the texture's repeats**:
  `top = anchor + R − (POS_Y + OFFSET_Y)/SCALE_Y` and `s0 = (POS_X − OFFSET_X)/SCALE_X` from the
  second vertex, where `R` is the bitmap's real height over `SCALE_Y` (15 steps for 60 rows at
  4 px a step, not the masked period's 16). `anchor` is 0 on a wall without the flag, a fixed
  height rather than the region's floor (a free wall in a region with floor 9 hides an attach at
  `POS_Y` 8 and shows one at `POS_Y` −24, z 10–11), the lowest lower edge among the wall's pieces
  at `POSITION` 0, and the highest upper edge at `POSITION` 1 — the pieces' *limited* edges, with
  no special case: against a solid 41…41 the room's 16 is the anchor and `POS_Y` 80 shows at
  11–12 while `POS_Y` 180 does not. It takes the ambient of the piece it lies on
  (OV-20261105-wall-light), and an attach on a sloped piece is cut to that piece's bounding
  heights (unmeasured: every carrier's piece ends were flat).
- **The manual's attach rule of thumb (p.42) puts an attach in the wall's texture space, and the
  wall's `OFFSET_X` and `OFFSET_Y` do slide it**: `s0` 2 steps from the second vertex with
  `OFFSET_X` 8 at 4 px a step, and the square's top 6 steps down with `OFFSET_Y` 8 on `POS_Y` 16.
  Where it differs from the manual is the zero: p.43 puts `POSITION` 0's vertical zero at the
  wall's lower edge, with a negative `POS_Y` raising the attach into view, but the origin is that
  edge plus one repeat `R` — measured `POS_Y` −4 at 4 px a step on a piece from 11 draws nothing,
  the square landing at 27–28.
- **A wall bitmap's rows are read through `v & (H−1)` and its columns through a plain modulo of
  W**: the manual (p.37) requires a power-of-two height and leaves the width free, and the
  original still reads a cut or odd-height bitmap through the mask of the next power of two. A
  12-row bitmap spans 16 rows: 0, 1, 2, 3, 0, 1, 2, 3, 8, 9, 10, 11, 8, 9, 10, 11, a period of
  16; a native 12×12 reads the same as the 12-row cut; the 16×8 control reads `v & 7`. Retail's
  one case in Apathy is the seats' 120×120 curtain, mask 119, a period of 128 rows, in which the
  bands 8–15, 24–31, … are never sampled, which is what draws its double stripes. Other archives
  carry such bitmaps too (NEWAGE's `ManForceField_tex` 14×14, START and TITLE's `wall1_tex`
  10×10).
- **An attach on a wall shows one phase at a time, from its own texture's `CYCLES`**: the
  original's two green blobs at Apathy's start read bright green and dark green in the frames
  sampled every two seconds.

## Open Questions
- `ATTACH` and ambient on `BELOW`-stack walls: no attach carrier stood beside a stack, and the
  code keeps the left region's ambient there (unmeasured).
- A `CYCLES` attach on a static base wall: the attach's batch takes its phase from the wall's
  `CYCLE`, which only advances while the wall's own texture has more than one. No carrier had a
  one-cycle base under a two-cycle attach.
- A facing region whose floor lies strictly between the two floors while its ceiling is above
  both — a solid that starts inside the room's height. The round-6 rule as written leaves the band
  between the lower piece's limited top and the upper piece's limited bottom undrawn. No carrier
  had that shape, and no retail wall does either: across the six archives 2697 walls have a piece
  clamped by round 6 and none of them opens such a band (round-6 probe, 2026-11-05).
- A floor's or ceiling's own read of a non-power-of-two bitmap is unmeasured. Where it shares a
  placed wall's bitmap (`wall1_tex` is a surface in START and TITLE) the code gives it the same
  period mapping as the wall, so both read the one expanded image rather than the surface tiling
  rows the image does not have.
- A run-time `SET` of a wall's `TEXTURE` to a bitmap no placed wall named at load time: it keeps
  the real image and the plain mapping, so the two agree, but its rows are then unmasked.
- `FENCE` and `CURTAIN`: no retail use and not implemented (`FENCE` sets `PORTCULLIS`, manual
  p.54, so its rows should follow the same anchors).
- The `pad_wall` carriers of the default fixture drew a yellow quad through a raised invisible
  edge (att-free-*), which is a view-test artifact, not a wall rule; not investigated.

## Implementation
`MeshBuilder` builds each wall as `WallPiece`s (`Floor1`, `Ceiling1`, `Floor2`, `Ceiling2`,
`Ambient`, and `Low`/`High`/`IsEmpty`): `WallPieces` cuts a two-region wall at its floors and
ceilings and, since round 6, limits each piece to the region it faces — the lower piece's top to
`min(higher floor, F's ceiling)`, the upper piece's bottom to `max(lower ceiling, C's floor)`, per
vertex — and picks each piece's ambient region (OV-20261105-wall-light). `EmitWallSegment` takes
each vertex's own `WallAnchor` (0 without the flag, the vertex's floor at `POSITION` 0 and its
ceiling at `POSITION` 1, on the limited edges). `EmitWallAttachment` runs on every wall (not a sky
one) and lays the attach once per non-empty piece, cut to that piece and lit as it — the pieces
cannot overlap now, so there is no union step — from `AttachAnchor` (0, the pieces' lowest lower
edge, or their highest upper edge) plus one real repeat.

`TextureLoader.RowMaskedWallTextures` names the bitmaps the loader expands and the mesh maps with
the period: a placed or declared wall's own texture, not a sky, not a power of two tall, and not
one a thing, actor or attach draws as a real image. It is worked out once per level, from the
walls as they stand when the images load, and cached, so a run-time re-texture cannot move a
bitmap into it after the fact. `TextureLoader.Load` expands exactly those (`MaskWallRows`, source
row `y & (H−1)`) and `MeshBuilder` reads the same set. `RowMaskedTextureMappingFor` gives both a
wall and a surface that reads such a bitmap a `v` span of `TextureLoader.WallRowPeriod`, so mapping
and image always agree, and everything else keeps `TextureMappingFor`.

`Wall.GeometryInvalidated` is raised by a run-time `SET` of a wall's `TEXTURE` or `ATTACH` and by
`WdlRuntime.SetObjectNumber` for the numeric `POSITION` and `PORTCULLIS` — only when the value
changes, so a script writing back what it read does not rebuild the mesh. `WdlRuntime` points it at
the same geometry invalidation `SHAKE` records, so the host rebuilds the mesh.

Tests: `MeshBuilderTests`
(`BuildWalls_PortcullisOnALowerPiece_StacksFromThePositionsEdge`,
`BuildWalls_PortcullisOnAnUpperPiece_StacksFromThePositionsEdge`,
`BuildWalls_PortcullisOnATwoPieceWall_AnchorsEachPieceItself`,
`BuildWalls_PortcullisOnASlopedEdge_KeepsTheAnchorsRowAlongIt`,
`BuildWalls_PortcullisWithOffsetY_SlidesTheAnchoredRows`,
`BuildWalls_PortcullisAgainstASolidRegion_AnchorsAtTheRoomsCeiling`,
`BuildWalls_PortcullisAgainstASolidRegionAtPositionZero_AnchorsAtTheFloor`,
`BuildWalls_PortcullisWithACeilingOffTheRepeat_AnchorsAtTheRoomsCeiling`,
`BuildWalls_AttachOnATwoRegionWall_IsCutToThePieceItLiesOn`,
`BuildWalls_AttachOnAFreeWall_MeasuresItsRepeatsFromHeightZero`,
`BuildWalls_AttachOnAPortcullisWall_MeasuresFromThePositionsEdge`,
`BuildWalls_AttachOnAPortcullisWall_UsesThePiecesOwnEdges`,
`BuildWalls_AttachFollowsTheWallsOffsets`,
`BuildWalls_AttachOnAWallToASolidRegion_StaysBelowTheRoomsCeiling`,
`BuildWalls_AttachOnAWallToASolidRegion_MeasuresFromTheRoomsCeiling`,
`BuildWalls_AttachOnAWallToASolidRegionAtPositionZero_MeasuresFromTheFloor`,
`BuildWalls_GapUnderARaisedPad_ShowsTheRoomsFaceAndThePadsStep`,
`BuildWalls_AttachOnARetailSignWall_ShowsBelowTheRoomsCeiling`,
`BuildWalls_AttachIsDrawnOnATwoRegionWall`, `BuildWalls_CyclingAttach_AnimatesWithTheWall`,
`BuildWalls_AttachRepeat_UsesTheBitmapsRealHeight`,
`BuildWalls_NonPowerOfTwoBitmap_ReadsTheMaskedRowPeriod`,
`BuildWalls_WallTextureSharedWithASprite_UsesThePlainMapping`,
`BuildWalls_WallRetexturedAtRunTime_KeepsTheLoadTimeSet`,
`BuildWalls_WallTextureSharedWithASurface_MapsBothWithThePeriod`,
`Build_SurfaceTextureOutsideTheWallSet_UsesThePlainMapping`,
`BuildWalls_WallToASolidRegion_TakesTheAmbientOfThePieceItFaces`,
`BuildWalls_SolidRegionOnTheLeft_ShowsTheRoomsFaceToItsCeiling`);
`TextureLoaderTests` (`WallRowPeriod_MaskedHeight_ReturnsTheNextPowerOfTwo`,
`RowMaskedWallTextures_NamesOnlyAPlacedWallsOwnBitmaps`,
`Load_NonPowerOfTwoWallTexture_RepeatsItsRowsUpToThePeriod`,
`Load_SpriteAndAttachTextures_KeepTheirRealRows`,
`Load_WallTextureSharedWithASurface_ExpandsTheImageItReads`); `WdlRuntimeTests`
(`Runtime_SetWallPositionToTheSameValue_KeepsTheGeometryInvalidationCount`);
`RuntimeHostWallPropertyTests` (`Frame_SetWallAttach_DrawsTheWallWithTheNewTexture`,
`Frame_SetWallTexture_DrawsTheWallWithTheNewTexture`,
`Frame_SetWallPosition_MovesTheAttachToThePiecesOtherEdge`).
