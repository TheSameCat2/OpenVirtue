# OV-20261015-branch-tail-jump: Does BRANCH return to the branching action?
Date: 2026-10-15
Status: confirmed
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
The 1998 A3 reference manual indexes `BRANCH` at p.79 and p.98, but p.79 describes only `GOTO`,
`CALL` and `END`. p.96 (`EACH_TICK`) mentions `BRANCH` only to forbid it towards actions that
contain `WAIT`. For `BRANCH target;` inside an action `A`:

1. Does the instruction after the `BRANCH` ever run?
2. Does the action that `CALL`ed `A` resume afterwards, or does `BRANCH` end the whole call chain?
3. If `target` reaches a `WAIT`, who resumes during the wait: `A`, or `A`'s caller?
4. Does a `BRANCH` through an `ACTION` synonym (re-pointed at run time with `SET`) behave like a
   direct name? What about a synonym that is `NULL`, or a name declared nowhere?
5. Does `BRANCH` inside an `IF { }` block or a `WHILE` loop leave the block, the loop and the action?
6. Do chained `BRANCH`es nest, so that a long chain hits a depth limit?
7. Does a `BRANCH` from an `EACH_TICK` action behave the same?

## Why It Matters
OpenVirtue ran `BRANCH` exactly like `CALL` (GitHub issue #7). The title's `menu_exec` is an
`IF_EQUAL menu_pos,n; BRANCH menun_act;` chain whose first target (`diff_menu`) moves
`menu_pos` to 4, so returning into the chain ran `menu4_act` too and New Game skipped the
difficulty menu. The retail enemy AI uses the same shape (`IF (…) { BRANCH a; } BRANCH b;`).
With a `CALL` the depth guard (64 nested actions) also bounds `BRANCH` chains.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: 1998 A3 manual p.79 (`GOTO`, `CALL`, `END`), p.80 (`WAIT`),
  p.96 (`EACH_TICK`), p.88–89 (`PRINTFILE`, `PRINT_STRING`, `PRINT_VALUE`); the retail WDL
  only to count and classify `BRANCH` uses (no script text is reproduced here). No source-level material.
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261015-branch-tail-jump/` (generated
  carriers, raw `OV*.TXT`/`OV*.PCX` outputs, `launches.md`).
- Relevant local-only file hashes (sha256): patched `VRUN-patched.EXE`
  `bba9f9bc6ee8b7bd3dc5bbd3c20f77afdaa5d4d0e2bfdf11dea6642508c324ae` (the same copy as
  OV-20261009-shoot-mapping).
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing
  under `_research/` is committed.

## Setup
- Original runtime: SaintsX-patched `VRUN.EXE` V3.963 (banner "professional release V3.963 - May 30 1998").
- Emulator/host: native Linux DOSBox-X 2026.10.01 (SDL1 build), `forcerate=60`, `aspect=false`,
  `output=surface`, `memsize=32`; carrier `work/` mounted as C:, launched as
  `VRUN.EXE -NJ TITLE -RUN > OVCON.TXT`, autoexec `exit` afterwards (the launcher of the
  projection and SHOOT probes).
- Fixture generator: `ovtool oracle prepare branch-semantics --runtime <VRUN-patched.EXE>`
  (`tools/OpenVirtue.Tools/BranchProbeCases.cs`). The `face` room of the projection probes only
  gives the runtime a valid world. One carrier per case, so a failure in one cannot hide the rest.
- Trace channel: after its usual settling `WAITT 16`, `IF_START` selects `OVBRN0.TXT`
  (`PRINTFILE "ovbrn", 0`), writes `BEGIN` and `CALL`s the case's entry action. Every action
  writes short tokens (`PRINT_STRING "E1\n"` …) to that file, so the token order is the order
  in which the statements ran. Afterwards `IF_START` writes `RET` and the end marker `OVEND0.TXT`.
- Cases (`E` = entry action, `T` = branch target, `RET` = written by `IF_START` after its `CALL` returns):
  - `fallthrough`: `E: E1; BRANCH T; E2` / `T: T1`.
  - `call-control`: the same with `CALL T`.
  - `wait`: `T: T1; WAITT 2; T2`; `IF_START` writes `RET`, waits `WAITT 8`, writes `LATE`.
  - `wait-control`: the same with `CALL T`.
  - `synonym`: `SYNONYM syn { TYPE ACTION; }`, `IF_START` does `SET syn, T`; `E: E1; BRANCH syn; E2`.
  - `blocks`: `E: E1; IF (z == 0) { BRANCH T; E2 } E3`, then a second entry
    `L1; WHILE (n < 3) { RULE n = n + 1; W; BRANCH T2; W2 } L2` (`T2` writes `T2`), followed by `RET2`.
  - `legacy-if`: skill `pos` = 1; `E: IF_EQUAL pos,1; BRANCH T1; IF_EQUAL pos,4; BRANCH T4; E2`,
    where `T1` writes `T1` and sets `pos` to 4, and `T4` writes `T4`.
  - `depth`: `loop: RULE n = n + 1; IF (n < 1000) { BRANCH loop; }`, no `WAIT`; `IF_START` prints `n`.
  - `unresolved-null`: the synonym case with `SET syn, NULL`.
  - `unresolved-name`: `BRANCH` to a name declared nowhere.
  - `each-tick`: `IF_START` does `SET EACH_TICK.1, K` and `WAITT 4`; `K: SET EACH_TICK.1, NULL; K1; BRANCH T; K2`.

## Observations
All runs 2026-10-15 18:58–19:02 UTC, eleven launches via `run-all.sh`. Ten exited with rc 0, all
outputs present and no compiler error in `OVCON.TXT`; `unresolved-name` is the exception noted below.

| Case | Trace in `OVBRN0.TXT` | Reading |
|------|-----------------------|---------|
| `fallthrough` | `BEGIN E1 T1 RET` | `E2` never ran; `IF_START` (the caller of `E`) resumed |
| `call-control` | `BEGIN E1 T1 E2 RET` | the documented `CALL` (p.79): the channel works |
| `wait` | `BEGIN E1 T1 RET T2 LATE` | at `T`'s `WAITT`, `IF_START` went on (`RET`); `T` finished later (`T2`); `E2` never ran |
| `wait-control` | `BEGIN E1 T1 E2 RET T2 LATE` | the documented `CALL` + `WAIT` (p.80): `E` resumed during `T`'s wait |
| `synonym` | `BEGIN E1 T1 RET` | same as a direct name |
| `blocks` | `BEGIN E1 T1 RET L1 W T2 RET2` | `E2`/`E3` never ran; the `WHILE` ran once (`W` once), and `W2`/`L2` never ran |
| `legacy-if` | `BEGIN T1 RET` | `T4` and `E2` never ran: the chain is not re-entered after `T1` changed `pos` |
| `depth` | `BEGIN RET N=1000.000` | 1000 chained self-`BRANCH`es without a `WAIT` all ran; no failure |
| `unresolved-null` | `BEGIN E1 E2 RET` | a `BRANCH` through a `NULL` synonym did nothing; `E` went on |
| `unresolved-name` | no trace file; `OVCON.TXT`: `Error(97): Missing keywords nonexistent/empty action br_none`, "Genesis failed", 1 error | rejected at compile time; the launcher's rc is 3 (no end marker) |
| `each-tick` | `BEGIN K1 T1 RET` | same from an `EACH_TICK` action: `K2` never ran |

## Conclusion
Confirmed for patched V3.963:

1. **`BRANCH target;` is a tail jump.** `target` runs *in place of* the current action. Nothing
   after the `BRANCH` in the current action runs, including the rest of any enclosing `IF`
   block or `WHILE` loop.
2. **Control then goes wherever the current action's own end would have gone.** If the
   branching action was `CALL`ed, its caller goes on after `target` ends, or as soon as
   `target` reaches a `WAIT`/`WAITT`, the same as for the `CALL` itself (p.79–80). `target`
   then finishes on its own after the wait. `BRANCH` does not end the caller's chain.
3. **Chains do not nest.** 1000 consecutive `BRANCH`es without a `WAIT` completed, so there
   is no nesting limit at least up to that depth.
4. **Targets resolve like `CALL` targets.** An `ACTION` synonym re-pointed with `SET` behaves
   like the direct name. **A synonym that is `NULL` makes `BRANCH` a no-op and the action goes
   on with the next instruction.** A name declared nowhere is a compile-time error (Error 97,
   "nonexistent/empty action"), so the runtime never sees one.
5. It behaves the same in an `EACH_TICK` action.

## Confidence
high: eleven cases, every trace matches exactly one of the readings predicted before the run,
and both `CALL` controls reproduce the documented `CALL` behavior on the same channel.

## Open Questions
- The compile error names "nonexistent/**empty** action". Whether `BRANCH` (or `CALL`) to a
  declared action with an empty body is also rejected was not probed.
- Depth beyond 1000, and a `BRANCH` cycle that never reaches a `WAIT` (presumably it hangs the
  original, as an endless `WHILE` would).
- `BRANCH` from a key hook (`IF_<key>`) or an object event (`IF_HIT`, `IF_NEAR` …) was not
  probed. Nothing here suggests it would differ, since the rule is about the current action only.
- Whether `EXCLUSIVE` treats the branched-to action as the same running action as the
  branching one (the tail jump suggests it does).

## Tests To Add Or Update
- `WdlRuntimeTests.Runtime_BranchInstruction_DispatchesNamedAction`: the next statement no
  longer runs (7, not 8).
- Runtime tests, one per case above: the caller of the branching action resumes; a `WAIT` in
  the target resumes the caller, and the target finishes later without returning; a synonym
  target; a `NULL` synonym is skipped; leaving `IF`/`WHILE`; the `IF_EQUAL` menu chain; 1000
  chained `BRANCH`es past the 64-deep `CALL` guard; `EACH_TICK`.
- `WdlInterpreterTests`: the direct (non-resumable) path stops after `BRANCH`.
- `RetailSmokeTests.Title_KeyboardThroughMenu_HandsOffToApathyAtItsDeclaredStart`: the first
  Enter on New Game shows the difficulty menu; the second starts at difficulty 3.

## Implementation Notes
`WdlInterpreter` replaces the running action's frames with the target's body when `BRANCH`
resolves (through `IWdlActionResolver`). The jump therefore reuses the same execution state:
it does not nest, and a `WAIT` in the target suspends the action the caller is already
waiting on. An unresolved target is skipped. Implementing the `NULL` case exposed a separate
bug: `SET syn, NULL` on an `ACTION` synonym went down the object-reference path and left the
synonym's old target in place. The object path now leaves keyword-valued synonyms to the
keyword path. Tests: `WdlRuntimeTests.Runtime_Branch_*`, `WdlInterpreterTests.Branch_*`.
