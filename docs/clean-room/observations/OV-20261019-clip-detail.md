# OV-20261019-clip-detail: Texture animation, BERKELEY walls, a wall's DIST and its CLIP_DIST border
Date: 2026-10-19 (round 1), 2026-10-20 (rounds 2–3)
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
OV-20261019-clip-contact left these `CLIP_DIST` questions open:

1. Outside its border, is a thing's or actor's texture animation paused, or does it run on with
   only `EACH_CYCLE` skipped?
2. Does `BERKELEY` (p.53: "exists only as long as it is viewed or the player is within its border
   distance") gate a wall's `EACH_TICK` and `IF_NEAR`?
3. Is a wall's `DIST` test gated by `CLIP_DIST` like a thing's?
4. Which `CLIP_DIST` gates the `EACH_TICK` of a wall between two regions with different values?

## Why It Matters
OpenVirtue gated walls by the smaller of the player's region's and both side regions'
`CLIP_DIST`, ran every wall's `DIST` test at any range, and treated `INVISIBLE` walls like visible
ones. Retail levels lower `CLIP_DIST` per region and hang triggers on long walls.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.30 (`CLIP_DIST`), p.51–53 (wall `CYCLE`,
  `DIST`, `IF_NEAR`, `BERKELEY`, `INVISIBLE`), p.63–64 (thing `CYCLE`), p.69–71.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261019-clip-detail/` (round 1 at the top,
  `round2/`, `round3/`; carriers, raw `OV*.TXT`/`OV*.PCX` outputs, emulator logs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae`. Traces `OVCDX0.TXT`:
  anim-gate `cdee4353…`, berkeley-wall `de82696c…`, wall-regions `80807bf5…`, wall-dist-clip
  (round 2) `740aecf1…`, wall-regions-visible (round 3) `f2baa539…`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963, native Linux DOSBox-X 2026.10.01,
  `forcerate=60`, `aspect=false`, `output=surface`, `memsize=32`, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`. Every run went at 8 ticks per cycle.
- Fixture generator: `ovtool oracle prepare clip-detail --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/ClipDetailProbeCases.cs`), on the room, sampler, walk and beam
  helpers of the proximity-events probe and the counting `EACH_TICK` of wall-pass-clip.
- Cases:
  - `anim-gate`: `CLIP_DIST 10`. A thing at (24, −8) and an actor at (24, 8) with a 4-phase
    texture (`DELAY 3` each) and counting `EACH_CYCLE`s. Synonyms let the sampler print both
    `CYCLE`s in every cycle. The player stands at the origin (32 from both), 4 from the thing,
    at the origin, 4 from the actor, at the origin, 48 ticks each.
  - `berkeley-wall`: passable free-standing walls at x = 10, a `BERKELEY` one (y −4…4, `DIST 4`,
    `IF_NEAR`/`IF_FAR`, counting `EACH_TICK`) and a plain one (y 8…16, counting `EACH_TICK`). The
    player faces them, turns away, faces them, stands 3 from the `BERKELEY` wall's nearer vertex
    and walks through it.
  - `wall-dist-clip` (round 2; the round-1 carrier did not compile, `Error(135)`, a skill and an
    action shared a name): `CLIP_DIST 10`. A passable wall from (30, −2) to (30, 2) with
    `DIST 20`, `IF_NEAR`/`IF_FAR` and a counting `EACH_TICK`. The player walks from the origin to
    x ≈ 24 and back, then through the wall from x = 26 to 34 and back.
  - `wall-regions`: room `CLIP_DIST 30`, pad `CLIP_DIST 10`. The pad's four boundary walls
    (`INVISIBLE`, `PASSABLE`) share a counting `EACH_TICK`, and a passable room wall from (12, −2)
    to (12, 2) has its own. Stands with `LOCATE` at (−8, 0), (−8, 8), in the pad at (−8, 16) and
    at (8, 0). `wall-regions-visible` (round 3): the same with visible pad walls, plus a
    free-standing `INVISIBLE` passable wall from (12, 6) to (12, 10) with a counting `EACH_TICK`.

## Observations
| Case | Trace | Reading |
|------|-------|---------|
| `anim-gate` | `CYCLE` 1 for both through the first stand at 32. Within 4 of the thing its `CYCLE` went 1 → 2 once in 48 ticks; back at the origin it stayed 2. Within 4 of the actor its `CYCLE` went 1 → 2; it stayed 2 at the origin. `EACH_CYCLE` counts stayed 0 | outside the border the phase is frozen, not run on. Inside it advanced far slower than `DELAY 3` suggests |
| `berkeley-wall` | `C` (plain) in every cycle. `B` (`BERKELEY`) in every cycle while facing the walls, none in the three cycles after turning away, back one cycle after turning to them again. `N` at `MY.DISTANCE` 3.000 on the beam to (8, 3), `F` 6.402 on the beam to (6, 0), `N` 4.770 on the walk through | a `BERKELEY` wall's `EACH_TICK` runs only while the wall is in view (one cycle late, so from the last drawn frame). Its `IF_NEAR`/`IF_FAR` work as for any wall |
| `wall-dist-clip` | walking in, one `N` at `MY.DISTANCE` 9.633 (x 22.3), not at 20. No `F` on the way back. Walking through the wall within the border: no `N`. `W` lines only while `MY.DISTANCE` < 10 | a wall's `DIST` test is gated by the border like a thing's: it fires on entering the border while within `DIST`, and leaving the border fires nothing |
| `wall-regions` | the room wall at 14, 22 and 26 with the player in the room; nothing at 26 in the pad. The `INVISIBLE` pad walls never ran, not even with the player in the pad | the `INVISIBLE` pad walls do not run their `EACH_TICK` (round 3) |
| `wall-regions-visible` | the visible pad walls ran at `MY.DISTANCE` 24 and 16 with the player in the room (`CLIP_DIST` 30), and at 8 in the pad. In the pad the room wall at 26 did not run. The `INVISIBLE` free-standing wall never ran, also at 10 from the player in the room | a wall between two regions is gated by the player's region's `CLIP_DIST` alone. An `INVISIBLE` wall runs no `EACH_TICK` |

## Conclusion
Confirmed for patched V3.963:

1. **Outside its border a thing's or actor's texture animation is paused.** Its `CYCLE` keeps
   its value and goes on from there (with clip-contact rule 3, no `EACH_CYCLE` either).
2. **A `BERKELEY` wall runs its `EACH_TICK` only while it is in view**, judged from the last
   drawn frame. Its `IF_NEAR`/`IF_FAR` are not affected. A `BERKELEY` thing or actor behaves
   differently (OV-20261019-clip-contact: only within `DIST`, no hooks).
3. **A wall's `DIST` test is gated by the `CLIP_DIST` border**, as a thing's is: outside it the
   wall keeps its near/far state, and coming within it while within `DIST` fires `IF_NEAR`.
4. **A wall's border is the player's region's `CLIP_DIST`**, also for a wall between two regions;
   the wall's own regions do not lower it. (A thing's or actor's is still the smaller of the
   player's region's and its own region's, OV-20261019-clip-contact.)
5. **An `INVISIBLE` wall runs no `EACH_TICK`.**

## Confidence
high for rules 3–5: each rests on several stands that all agree. medium for rule 1 (one probe;
the slow phase advance inside the border is not understood) and rule 2 (the "within its border
distance" half of p.53 was not separated from being in view: every stand within `DIST` also faced
the wall).

## Open Questions
- Why the texture advanced only once in 48 ticks with `DELAY 3` at 8 ticks per cycle.
- Whether a `BERKELEY` wall the player stands near but looks away from runs its `EACH_TICK`.
- Whether a wall's pass-through or touch `IF_NEAR` fires outside the border (only crossings inside
  it were measured), and whether an `INVISIBLE` wall's `DIST` test runs.

## Tests To Add Or Update
- `WdlRuntimeTests.Tick_PlayerProximity_WallOutsideClipDistIsNotDistTested`.
- `WdlRuntimeTests.Tick_ObjectEachTick_WallBetweenRegionsUsesThePlayersRegionClipDist`.
- `WdlRuntimeTests.Tick_ObjectEachTick_InvisibleWallRunsNothing`.
- The existing texture-animation gate tests already cover rule 1.

## Implementation Notes
`WdlRuntime.ClipBorder` returns the player's region's `CLIP_DIST` for a wall and the smaller of
that and the object's own region's for a thing or actor. `DispatchProximityEvents` skips the
`DIST` test of a wall outside its border and keeps its state; the touched-wall path still runs.
The object `EACH_TICK` loop skips `INVISIBLE` walls. `BERKELEY` walls are not implemented: the
engine has no per-wall view test yet.
