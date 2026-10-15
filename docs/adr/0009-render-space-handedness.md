# ADR-0009: Render space is (map x, height, −map y); camera yaw = PLAYER_ANGLE + π/2
Date: 2026-10-09
Status: accepted

## Context

Gameplay code (`Player`, `WdlPlayerMotion`, `WdlRuntime` movement, AI, `SHOOT`, `ROTATE`,
`DROP`, `SHIFT`, snapshots) keeps the WMP plan view in a `Vector3` as **(map x, height,
map y)**: a right-handed, y-up map in which `PLAYER_ANGLE` 0 faces +x and angles grow
counter-clockwise (`Math.Cos(angle)`, `Math.Sin(angle)` everywhere, ~25 call sites).

The renderer uses a right-handed look-at (`Matrix4x4.CreateLookAt`) with render +Y up and
needs a render-space vector per world point. Until this decision the render side reused the
gameplay vector unchanged (render z = map y). With a right-handed look-at that puts map +y on
the **screen right** when facing map +x.

The original does the opposite. OV-20261009-tilt-model-shear-vs-pitch read the asymmetric
far wall of the tilt room (red at map y −12..−8 … green at 8..12) off the DOSBox-X
screenshots that show it (the tilt 0 and −0.5 runs; the tilt 1.0 frame is floor only):
left→right the stripes read G R G R G R and the world-y fit is
`x = 319.6 − 12.2·y`. **Facing map +x, the map's +y side is on the screen left.** This is
the natural consequence of a right-handed, y-up map (north is on the left when you face east).
The hitscan already matched it: positive `SHOOT_X` turns clockwise, toward map −y when facing
+x, which is the original's screen right. So the D3D11 frame was mirror-imaged relative to
both the original and our own hitscan.

A second, related defect: at level load `LevelWindow.PlaceCameraAtStart` derived the camera
yaw as `−start.Angle · π/180`, a convention unrelated to the one `SyncPlayerRuntimeState`
used to report `PLAYER_ANGLE` (`π/2 − yaw`). A WMP start angle of 0 therefore faced map +y
and the runtime reported `PLAYER_ANGLE = π/2`, while the oracle dump for `PLAYER_START 0 0
0 0` reads `ANG=0.000` facing +x.

## Decision

1. **Render space is `(map x, height, −map y)`.** The conversion lives in one place,
   `OpenVirtue.Engine.Rendering.RenderSpace` (`FromMap`, `ToMap`, `ZFromMapY`), and is applied
   only where map coordinates become render vectors: `MeshBuilder` (wall quads and floor/
   ceiling surfaces), `SpriteBuilder.BasePosition`, and in the host the camera position, the
   back-to-front sprite order, the sprite click test and the spatial-sound pan. Gameplay
   `Vector3`s are unchanged; `SpriteBuilder`'s `viewerPosition` (directional sides) and the
   hitscan stay in map space.

2. **Camera yaw is measured around render +Y with `forward = (sin yaw, 0, cos yaw)`, and
   `yaw = PLAYER_ANGLE + π/2`** (`RenderSpace.YawFromPlayerAngle` / `PlayerAngleFromYaw`,
   the latter normalized to `[0, 2π)`). Check: angle 0 → yaw π/2 → forward (1, 0, 0) = map +x;
   angle π/2 → yaw π → forward (0, 0, −1) = map +y. Both yaw producers in the host
   (`PlaceCameraAtStart`, `ApplyRuntimePlayerState`) and the consumer (`SyncPlayerRuntimeState`)
   use these two functions and nothing else.

3. **The WMP `PLAYER_START` angle is degrees, counter-clockwise, 0 = map +x**, the same
   convention as the THING/ACTOR placement angle (`MapEntity.FacingAngleRadians`; manual
   ch.04 p.17 gives the WMP angle in degrees, p.67 says a WDL `ANGLE` is added to it).
   `PlayerStart.AngleRadians` exposes it and `WdlRuntime` seeds `PLAYER_ANGLE`, `PLAYER_SIN`,
   `PLAYER_COS` from it at construction, so headless consumers and the host agree without
   a sync. Only a = 0 has been probed (every OV-20261009 fixture); the sense for other values
   follows the placement convention until a probe says otherwise.

4. **Screen right stays `cross(forward, up)`**, the look-at view's x-axis
   (`RenderSpace.ScreenRight`). With the z flip this is map −y when facing +x, i.e. the
   original's screen right, so billboards, the click test, the audio pan and
   `ProjectionModel.Project` need no mirroring and keep agreeing with each other.

5. **Texture coordinates are not flipped by this decision.** `u`/`v` follow the map axes
   exactly as before; only the position's z changes sign. The original's wall texture
   orientation was unprobed when this was written; adding a deliberate flip then would have
   been a guess. *Addendum 2026-10-10 (OV-20261009-shoot-mapping):* thing bitmaps are drawn
   unmirrored (left half on the screen left), as the renderer already did; a wall viewed from
   its left region shows the bitmap's left half at the wall's **second** vertex, so
   `MeshBuilder` now puts u = 0 at v2 growing toward v1 and the wall `HIT_X` counts from v2
   (engine commit `2a817da`). The right-region view and the `OFFSET_X` sense stay open.

6. The sky panorama side is selected from `PLAYER_ANGLE` (a compass quantity), numbered
   clockwise like directional sprite sides, instead of from the render yaw. Which side the
   original anchors at angle 0 remains unprobed (roadmap: exact panorama stitching).

## Alternatives considered

- **Mirror in NDC or use a left-handed view** (negate the projection's `M11`, or build the
  view with `cross(up, forward)`). One line, but it silently inverts everything that reasons
  about screen right in world space: billboards, the sprite click test, the audio pan and the
  `Project()` tests would all flip and need compensating sign changes. The z flip keeps one
  right-handed frame end to end. Rejected.
- **Negate map x instead of map y** (render x = −map x). Equally consistent, but `PLAYER_ANGLE`
  0 = +x is the convention the whole runtime and the manual share; keeping render +x = map +x
  makes the yaw mapping a quarter turn and nothing else. Rejected.
- **Change the gameplay vector layout** to `(map x, map y, height)` or `(x, h, −y)`
  throughout. Touches ~25 gameplay sites, the WDL reflective `my.x/y/z` bridge and the save
  snapshots for no parity gain. Rejected.
- **Fix the yaw formula only** (keep render z = map y, flip the yaw sign). Makes the start
  angle consistent but leaves the mirror image. Rejected.

## Consequences

- The D3D11 frame now shows the world with the original's handedness; the hitscan's lateral
  sign (positive `SHOOT_X` = screen right), the click test and the pan were already correct
  and are unchanged in behavior. (The hitscan's `SHOOT_Y` sign and `HIT_DIST` semantics were
  corrected the next day by OV-20261009-shoot-mapping, independently of this decision.)
- The apparent mouse-look direction changes: `PLAYER_ANGLE` increasing (counter-clockwise on
  the map) now turns the view left, as it must; before, it slid the mirrored scene the wrong
  way. The host's input signs were retuned with it (2026-10-14): a rightward mouse motion now
  yields a negative `FORCE_ROT` (clockwise, like the cursor-right key), and because positive
  `PLAYER_TILT` looks down, PgUp and mouse-forward yield a negative `FORCE_TILT` (look up).
  The retail movement script adds positive multiples of both forces to `PLAYER_VROT` /
  `PLAYER_TILT` (sanitized keyword scan); the sign the original's keyboard driver gives
  `FORCE_TILT` for PgUp is inferred from that, not probed.
- The camera adopts the runtime's `PLAYER_ANGLE`/`PLAYER_TILT` at level start and after a
  save is loaded (`AdoptRuntimeHeading`), so the skills, not a stale camera, are the source
  of truth in both places.
- Triangle winding of every world triangle inverts. The rasterizer runs `CullMode.None`, so
  nothing is culled. The GL host disables face culling for the same reason.
- The GL renderer (ADR-0008) consumes `RenderSpace` and `ProjectionModel` from the engine;
  it must not introduce a second mapping.
- Engine-level tests pin the handedness without a GPU: `RenderSpaceTests`,
  `WorldHandednessTests` (mesh and sprite output × look-at × `CreateProjectionMatrix` land
  map +y on the left half and map −y on the right half; the tilt probe's stripe edges come
  out in descending map-y order at the measured `319.6 − 12.2·y` columns; the runtime seeds
  `PLAYER_ANGLE` from the WMP start). `ProjectionModelTests.CreateProjectionMatrix_WithLookAtView_ScreenRightIsCrossOfForwardAndUp`
  must stay green.
- Visual confirmation against the oracle screenshot
  (`_research/oracle-runs/OV-20261009-tilt-model-shear-vs-pitch/S640x480-tilt-0/work/OVTLT0.PCX`,
  stripes G R G R G R left→right) was done on the Linux development machine on 2026-10-15.
  `Tilt0FrameTests` drew the room with the GL host at 640×480. The far-wall edges landed at
  columns 0, 174, 222, 271, 320, 369, 418, 466, within one pixel of the capture, and the floor
  was at the bottom of the frame. The math in this ADR was not retuned.
- The Linux oracle-host ADR that ADR-0008 reserved as "0009" becomes ADR-0010.
