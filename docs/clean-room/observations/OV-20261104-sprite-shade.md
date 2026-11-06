# OV-20261104 — A sprite's shade: the player's light term less two

Status: confirmed (four rounds, fifteen carriers) and implemented.

## Question
OV-20261029-shading measured the shade level of walls and floors by depth. Things and actors are
drawn as sprites; are they shaded by the same curve, by their depth or their distance, with one
level over the whole sprite, and what of a texture's `AMBIENT` and the ends of the range?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Fixture generator: `ovtool oracle prepare sprite-shade <dir>` (`tools/OpenVirtue.Tools/SpriteShadeProbeCases.cs`).
  A thing of a 32×48 bitmap in the gray ramp's first colour (index 16, `RANGE 16, 32`) stands at
  depths 12 (below the screen), 20, 28, 36 and 44, at depth 28 twelve steps to the left (distance
  30.5), at 28 with its texture's `AMBIENT` 0.25, at depths 15.2, 19, 30.4 and 38 where the walls'
  formula lands on an exact even level, at 28 with `PLAYER_LIGHT` 0, and at 20 with region
  `AMBIENT` 0.5, and at 28 and 20 with `PLAYER_LIGHT` 0 and 0.1 under region `AMBIENT` 0.25; the
  default `PLAYER_LIGHT` 1 and `LIGHT_DIST` 10 otherwise. The final
  screenshot's indices over the sprite are 16 plus its level. Traces (`OVSPS0.TXT`, two lines)
  hash `dce49209`.
- Confirmation that no proprietary code/assets are committed: synthetic level and bitmap only;
  nothing under `_research/` is committed.

## Readings
Every sprite reads one index. The walls' formula is 30 − 32·AMBIENT − 30.4·PLAYER_LIGHT·LIGHT_DIST/d
rounded down to an even level (OV-20261029-shading).

| Depth | Case | Walls' formula → even | Sprite level |
|---|---|---|---|
| 20 | | 14.8 → 14 | 16 |
| 28 | | 19.14 → 18 | 20 |
| 36 | | 21.56 → 20 | 22 |
| 44 | | 23.09 → 22 | 24 |
| 28 (distance 30.5) | 12 steps aside | 19.14 → 18 by depth (20.02 → 20 by distance) | 20 |
| 28 | texture `AMBIENT` 0.25 | 11.14 → 10 | 12 |
| 15.2 | | 10 exactly | 12 |
| 19 | | 14 exactly | 16 |
| 30.4 | | 20 exactly | 22 |
| 38 | | 22 exactly | 24 |
| 28 | `PLAYER_LIGHT` 0 | 30 | 30 |
| 20 | region `AMBIENT` 0.5 | −1.2 → −2, held at 0 | 0 |
| 28 | `PLAYER_LIGHT` 0, region `AMBIENT` 0.25 | 22 | 22 |
| 20 | `PLAYER_LIGHT` 0.1, region `AMBIENT` 0.25 (light term 1.52) | 20.48 → 20 | 22 |
| 20 (6 aside) | OV-20261104-genius round 3: `PLAYER_LIGHT` 0, `AMBIENT` 0.25, a lamp near, the texture with and without `ALBEDO` | 22 | 22 |

- **A sprite is shaded by its depth along the line of sight, one level over the whole sprite,
  by the walls' formula with the player's light term two levels less, not below 0**:
  30 − 32·AMBIENT − max(30.4·PLAYER_LIGHT·LIGHT_DIST/d − 2, 0), rounded down to an even level.
  Where the light reaches the sprite it is two levels darker than a wall there (an exact 14
  reads 16); where it does not (`PLAYER_LIGHT` 0, or a term under 2) it reads the walls' level.
- A texture's `AMBIENT` brightens a sprite as it does a wall (8 levels a 0.25); the lateral
  sprite's level follows its depth, not its distance; a region's `GENIUS` lamp and the texture's
  `ALBEDO` do nothing to a sprite (OV-20261104-genius round 3).

## Open Questions
- Actors (the same path in OpenVirtue), attach layers, and a sprite past level 30 (a negative
  `AMBIENT`): the `INFINITY` colour as a wall shows, or held at 30.

## Addendum (2026-11-06): the rule is the floor form of the zones
The **A sprite is shaded by its depth** rule above is the same rule as a floor's, with the light's
zone count floored instead of ceiled and one zone more (OV-20261105-shade-global): a sprite takes
`m = ⌊Qw/d⌋` where a floor takes `⌈Qf/d⌉`, and the level is `2·⌊(30 + φ − 32·A − 2·m)/2⌋`. The
"two levels less" was that difference seen at one light, not a constant: the whole table above is
reproduced by the floor form. The depths 15.2, 19, 30.4 and 38, chosen as "exact even" under the old
`k` 30.4, are simply those where `Qw/d` sits just under 10, 8, 5 and 4 zones, which is a property of
that `k` and not of the engine. What stands: one level over the whole sprite, by its depth and not
its distance, `AMBIENT` brightening it as a wall, a lamp and `ALBEDO` doing nothing.

## Implementation
`PaletteShading.SpriteLevel` (⌊Qw/d⌋ zones; `SpriteBias` is gone with the addendum above) and
`PaletteShading.SpriteMarker`, which marks a sprite's batch so the shader floors where a surface
ceils; the host sets the marker on sprite batches instead of a shade bias. Tests:
`PaletteShadingTests.SpriteLevel_FloorsTheWallZones`, `RuntimeHostBlendTests`.
