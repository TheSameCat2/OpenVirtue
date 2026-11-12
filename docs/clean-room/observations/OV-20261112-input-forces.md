# OV-20261112-input-forces: Held keys and mouse motion as movement forces
Date: 2026-11-12
Status: confirmed and implemented
Observer: Claude Code (Opus 5.5)
Implementer: same

## Question
What do `FORCE_AHEAD`, `FORCE_ROT`, `FORCE_STRAFE`, `FORCE_TILT` and `FORCE_UP` read, cycle by cycle, while keys
are held and the mouse moves? The manual (p.119-120) names the sources: cursor keys, [,] and [.], [Pg up]/[Pg dn],
[Home]/[End], the mouse, and [Alt] with the mouse for strafing. It also names `KEY_SENSE`, `SHIFT_SENSE` and
`MOUSE_SENSE`. It does not give the mouse's scale, whether a held key ramps, the signs of strafe and tilt, or how
keys and the mouse combine.

## Why It Matters
Every retail movement action turns these skills into the player's motion. OpenVirtue's host took 16 mickeys for a
full force, clamped it to ±1, set a held key's force at once, gave [.] the positive strafe and moved the player
with WASD and Space. Mouse turning felt wrong, and S, D and Space also fired Saints of Virtue's own bindings.

## Clean-Room Boundary
- Oracle used: native Linux DOSBox-X 2026.10.01, `forcerate=60`, `autolock=true`, on its own Xvfb display (:97)
  with `SDL_VIDEODRIVER=x11`, and the SaintsX-patched `VRUN.EXE` V3.963.
- Input: `tools/oracle/xtest_input.py` holds keys and sends relative mouse motion through XTest on that display
  only. It clicks once to lock the mouse. AUTOTYPE cannot hold a key.
- Reference material consulted: the 1998 manual p.107 (`MOUSE_MODE`), p.119-120 (the `FORCE_*` skills and
  the `*_SENSE` skills).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261112-input-forces/` (carriers, `drive.sh`, input logs).
- Relevant local-only file hashes (sha256, first 8), `OVINP0.TXT`: defaults `0273d9b6`, sense `3c180f20`,
  round2/feedback `e3e3d2e5`.
- Confirmation that no proprietary code/assets are committed: only numbers the original printed.

## Setup
`ovtool oracle prepare input-forces` (`tools/OpenVirtue.Tools/InputForcesProbeCases.cs`). A global `EACH_TICK`
sampler prints `TICKS`, the cycle, `TIME_CORR`, the five forces × 1024, `MICKEY_X`/`MICKEY_Y`, `KEY_SHIFT`,
`KEY_ALT` and the cursor keys every cycle, for 4800 cycles at about 60 a second (`TIME_CORR` 0.25 to 0.28).
- `defaults`: the runtime's own `KEY_SENSE` 1, `SHIFT_SENSE` 2, `MOUSE_SENSE` 1. The "basic" schedule holds each
  key for 1.5 s (up, Shift+up, right, Shift+right, [,], Alt+right, Pg up, Home, End). Then it moves the mouse 1, 4,
  16 and 64 pixels a step at 60 steps a second, then left, up and down, with Alt, with up held, and one burst of 40.
- `sense`: the same with `KEY_SENSE` 0.5, `SHIFT_SENSE` 3, `MOUSE_SENSE` 2.
- `round2/feedback`: a second `EACH_TICK` multiplies `FORCE_AHEAD` by 1.3 after the sample, as Apathy's movement
  action does. The "order" schedule presses keys one after the other and puts a key and the mouse on one axis.

## Observations
| Input | Readings (× 1024, per cycle) |
|---|---|
| cursor up held | 256, 512, 768, 1024, 1024 …; 0 the cycle it is let go |
| cursor right held | −204, −408, −612, −816, −1020, −1024 |
| [,] held | `FORCE_STRAFE` 256 … 1024 (positive) |
| Alt + cursor right | `FORCE_STRAFE` −204 … −1024; `FORCE_ROT` stays 0 |
| Pg up / Home / End | `FORCE_TILT` −204 … −1024 / `FORCE_UP` 256 … 1024 / `FORCE_UP` −256 … −1024 |
| Shift and up pressed together | 256, 1024, 2048; right: −204, −816, −2040, −2048 |
| Shift held first, then up | 512, 1536, 2048 |
| up held, then Shift | 1024, then 2048 the next cycle |
| sense: up, right, Shift+up, Shift+right | 256, 512 · −204, −408, −512 · 256, 1536 · −204, −1224, −1536 |
| feedback: up held, script ×1.3 after the sample | 256, 512, 768, 1024 (the same) |
| up then down held, [,] then [.] held | the first key's 1024 stays |
| mouse right n mickeys in a cycle | `FORCE_ROT` −16·n: 1 → −16, 4 → −64, 64 → −1024, 128 → −2048; `MICKEY_X` = n |
| sense: mouse right | −32·n (`MOUSE_SENSE` 2); `KEY_SENSE` plays no part |
| mouse up / down n | `FORCE_AHEAD` +16·n / −16·n |
| Alt + mouse right n | `FORCE_STRAFE` −16·n; `FORCE_ROT` keeps the value it had before Alt went down (−112 for a whole segment) |
| right held + mouse right or left 8 a cycle | `FORCE_ROT` the key's −204 … −1024; the mouse adds nothing |
| up held + mouse up 8 | `FORCE_AHEAD` the key's 256 … 1024 |
| a cycle with no mickeys | that force 0 at once |

## Conclusion
- **A held key ramps its force by a fixed step each cycle:** 256/1024 for cursor up/down, [,]/[.] and Home/End,
  204/1024 for cursor left/right (turning, or strafing with Alt) and Pg up/Pg dn. Each cycle,
  `force = min((previous + step)·shift, KEY_SENSE·shift)`, where `shift` is `SHIFT_SENSE` while Shift is down
  and 1 otherwise. `previous` is the runtime's own last value, not the skill a script changed. Letting go gives 0.
  With both keys of an axis down, the force keeps its way. Shift pressed together with the key comes in a cycle
  later.
- **Signs:** cursor up, cursor left, [,], Home are positive; [,] strafes left, Pg up tilts up.
- **The mouse gives `mickeys·MOUSE_SENSE·16/1024`, unbounded,** negated so that right turns right
  (`FORCE_ROT` negative) and forward walks forward (`FORCE_AHEAD` positive). With Alt, sideways motion goes
  to `FORCE_STRAFE`, and `FORCE_ROT` is left as it was. A held key on an axis overrides the mouse there.
  `MOUSE_MODE` 2 takes the mouse off the forces (manual p.107).

## Confidence
High: every reading in the three runs fits these rules to the unit.

## Round 3: a slow emulator
`round3/slow` is the defaults carrier with DOSBox-X `cycles=fixed 6000`, so the runtime ran at `TIME_CORR` 8 (about
two cycles a second), trace cut to 700 cycles. `OVINP0.TXT` `02e79d16` (the run hit its timeout after 90 cycles, enough
for the key segments). **The ramp is per cycle, not per tick**: cursor up read 256, 512, 768, Shift+up 256, 1024,
2048, cursor right −204, −408, −612, as at 0.25 ticks a cycle. The mouse stays 16/1024 a mickey and unbounded
(1866 mickeys in one cycle gave −29856/1024).

## Open Questions
- Joystick forces.

## Implementation
`WdlInputForces.Mouse` and `WdlInputForces.KeyRamp` (engine); `RuntimeHost.UpdateMovementForces`, once a frame,
with a `KeyAxis` per force. Tests: `WdlInputForcesTests`, `RuntimeHostInputTests`.
