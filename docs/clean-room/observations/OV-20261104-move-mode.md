# OV-20261104 — MOVE_MODE: what 0.5, 0 and −0.5 stop

Status: confirmed (four carriers, fast launches) and implemented.

## Question
The retail title's `start_game` sets `MOVE_MODE` 0 (and `RENDER_MODE` 0) for its menus, and
restores a saved value after. The manual (p.104) grades it: 0.5 stops the actors' movement and
the objects' `EACH_TICK` actions; 0 the player's movement and all object and region events too;
−0.5 every action but the keyboard events. OpenVirtue only declared the skill. Which cycle parts
stop at each value, from which cycle, and does a key hook still fire and restore it?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963; about 0.26 ticks a cycle.
- Fixture generator: `ovtool oracle prepare move-mode <dir>`
  (`tools/OpenVirtue.Tools/MoveModeProbeCases.cs`). A global `EACH_TICK` sampler prints every
  cycle: `TICKS`, its count, `MOVE_MODE`, the walking player's X (`PLAYER_VX` 0.5 a cycle), a
  way actor's X (0.2 a tick on (30, 12) ↔ (46, 12)), and the counts of a thing's, the actor's and
  the room's `EACH_TICK`, the second global slot, a `WAIT 1` loop (`CALL`ed) and `EACH_SEC`.
  After a 16-tick stand the probe action sets the mode (`A` before, `B` after), waits 16 ticks
  (`D`), sets 1 and waits 16 more (`E`). DOSBox-X presses Space about four seconds in: `IF_ANYKEY`
  prints `K` and sets `MOVE_MODE` 1, in case the action is frozen.
- Traces (`OVMVM0.TXT`, sha256 first 8): control `98fa43bf`, half `3f76aeb7`, zero
  `b7e472fd`, minus `c33aa080`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Readings
| Carrier | Reading |
|---|---|
| control (1) | the player walks 0.5 a cycle to the east wall, the actor patrols, every counter counts every cycle, `K` once |
| half (0.5) | from the cycle of the `SET` the actor's X and the thing's, actor's and room's `EACH_TICK` counts hold (108) while the player walks on, the second global slot, the `WAIT` loop and `EACH_SEC` count on and the action's `WAITT 16` ends on time (`D`); from the cycle that sets 1 they all count again (119 eleven cycles on) |
| zero (0) | as half, and the player's X holds (29) from the cycle of the `SET` and walks on from the cycle that sets 1 |
| minus (−0.5) | no `C` line after `B`: the sampler (global slot 1) stopped, as did the second slot, the `WAIT` loop, `EACH_SEC` and the action's `WAITT`; `TICKS` went on (`K` at 81.8, 53 ticks after `B`); the key fired, set 1, and everything resumed in that cycle: `D` 15.2 ticks after `K` (the wait counted nothing while frozen), the next `EACH_SEC` 2.6 ticks after `K`, where the second had 2.6 ticks left at `B` (its clock stood still too) |

- **0.5 takes the regions, actors, things and walls out of the cycle** (their `EACH_TICK`s, the
  actors' steps), live from the cycle that sets it and back from the cycle that sets 1. The
  global slots, `EACH_SEC`, the waits and the actions go on.
- **0 stops the player's movement as well**, and (manual) the object and region events.
- **−0.5 stops everything but `TICKS` and the key hooks**: no resumed wait, no global slot, no
  `EACH_SEC` (its clock frozen), no count-down.

## Implementation
`WdlRuntime.Tick`: below 0 it dispatches the queued hooks after `UpdateTimingSkills` and returns;
at 0 and 0.5 it skips `RunObjectPass`, at 0 the region and proximity events too. `RuntimeHost`
skips the player's motion and the contact events at 0 and below. Test
`WdlRuntimeTests.Tick_MoveMode_FreezesObjectsAtAHalfAndEverythingButTicksAndHooksBelowZero`.
half and zero replay 179 of 180 lines (one `EACH_SEC` count at the seconds clock's start offset),
control and minus to the way actor's launch-phase X (0.002 off, the clock family).
