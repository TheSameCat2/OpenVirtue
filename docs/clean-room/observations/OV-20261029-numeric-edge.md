# OV-20261029-numeric-edge: the number format and arithmetic edge cases
Date: 2026-10-29
Status: confirmed (one round, seven carriers)
Observer: OpenVirtue maintainers (black-box probe)
Implementer: OpenVirtue maintainers

## Question
How does the original store skill values and compute with them: precision, range, rounding of
literals and of negative values, `INT`, `%` and the bitwise operators on fractions and negatives,
functions near or outside their domain, and what happens on a division by zero?

## Why It Matters
OpenVirtue computed in IEEE doubles. Any script that accumulates small steps, compares fractions,
divides, or relies on overflow behaves differently. `MY.DISTANCE` reading 65535.999 for far things
(OV-20261019-actor-contact) hinted at a fixed-point format; the compiler's own error for printing a
flag (`Error(66): Wrong typ MY.PLAY FIXED FLAGS`, OV-20261029-motion-timing) names the type FIXED.

## Clean-Room Boundary
- Oracle used: DOSBox-X run of the SaintsX-patched DOS runtime (V3.963), file-output trace markers.
- Reference material consulted: the 1998 A3 manual p.76 (operators and functions; `%`, `|`, `^`,
  `&` "for integer values only"), p.88 (`PRINT_VALUE` writes three decimals).
- Split-role mode used: not needed (black-box).
- Local-only artifacts: `_research/oracle-runs/OV-20261029-numeric-edge/`.
- Relevant local-only file hashes (sha256, first 8): patched `VRUN.EXE` `bba9f9bc`; `OVNUM0.TXT`:
  values `e3fa732a`, logic `7f7a8623`, sqrt-neg and log-zero `559f77a3`, asin-two `c349a7b5`.
- Confirmation that no proprietary code/assets are committed: synthetic carriers only.

## Setup
Native Linux DOSBox-X 2026.10.01, `forcerate=60`; carriers from `ovtool oracle prepare numeric-edge`
(`tools/OpenVirtue.Tools/NumericEdgeProbeCases.cs`). `values` assigns each expression to a skill
and prints it (negative operands are written `0 - x`); precision is read from scaled values.
`logic` tests conditions. The risky cases run one operation in a `CALL`ed action between a `B` and an
`A` line, with `C` printed by the caller.

## Observations
| Expression | Printed | Reading (raw = value × 1024) |
|---|---|---|
| `1/3`, ×1024, ×65536 | 0.333, 341.000, 21824.000 | 1/3 is 341/1024 |
| `0.001`, ×10⁶ | 0.001, 976.563 | the literal is 1/1024 |
| `0.0001`, ×10⁶ | 0.000, 0.000 | the literal is 0 |
| `32767+2`, `65535+2` | 32769, 65537 | no 16-bit limit |
| `2097151+2` | −2097151.000 | the sum wraps at 2²¹ |
| `1000000*1000` | 1755648.000 | the 64-bit raw product, shifted, wraps to 32 bits (10⁹·1024 mod 2³² / 1024) |
| `0 - 32769`; `0 - 2097153` | −32769.000; −2097151.999 | the literal 2097153 saturates at 2097151.999 |
| `7/2`, `(0-7)/2` | 3.500, −3.500 | |
| `INT(2.7)`, `INT(0-2.7)`, `INT(0-2.5)` | 2, −3, −3 | `INT` rounds down |
| `7%3`, `(0-7)%3`, `7%(0-3)`, `7.9%3`, `7%2.5` | 1, 0, 1, 1.899, 2 | raw remainder; the dividend's bits taken unsigned (−7168 as 2³²−7168 is a multiple of 3072), the divisor's magnitude |
| `5.7&3`, `(0-1)&255`, `4\|2.9`, `6^3` | 1, 255, 6.899, 5 | bitwise on the raw bits, fraction included (2.9 is 2969 raw) |
| `SIGN(0)` | 0 | |
| `TAN(1.5707)` | 2066.854 | the argument is 1608/1024 = 1.5703, the result truncated |
| `EXP(20)` | −2097151.999 | out of range |
| `SIN(0.5)·10⁶`, `SQRT(2)·10⁶` | 478515.625, 1414062.500 | results truncated to 490/1024 and 1448/1024 |
| logic: `0.1*3 == 0.3`, value of `0.1*3` | 0, 0.299 | 306/1024 against 307/1024 |
| logic: `0.0001 > 0`, `IF (0.0001)`, `0.0005 > 0`, `IF (0 - 1)` | 0, 0, 0, 1 | literals truncate (0.512 raw is 0); a negative value is true |
| `SQRT(0-1)`, `LOG(0)` | `B 12345`, `A −2097151.999`, `C −2097151.999` | an undefined result reads −2097151.999 and the action goes on |
| `ASIN(2)` | `A 1.570` | the argument is clamped to 1 |
| `1/0`, `7%0` | `OVCON.TXT`: `>>> Division by zero at RULE DO_ACTION ne_sub <<<` and the engine shut down; the buffered trace was lost (no `BEGIN`) | a fatal error |

`PRINT_VALUE` rounds to three decimals (0.298828 printed 0.299, 976.5625 printed 976.563).

## Invariant
A skill is a signed 32-bit fixed-point value with 10 fractional bits (steps of 1/1024, from −2097152
to 2097151.999). A literal is truncated toward zero and saturates at the limits. `+`, `−` and `*` wrap
around in 32 bits; `*` keeps the full product before dropping the low 10 bits. `/` divides the raw
values (truncated). `%` is the remainder of the dividend's raw bits taken as unsigned by the
divisor's magnitude. `&`, `|`, `^` act on the raw bits. `INT` rounds down. A function computes from the
fixed argument and truncates its result; an undefined or out-of-range result reads −2097151.999;
`ASIN`/`ACOS` clamp their argument. A division by zero is a fatal error.

## Open Questions
- Rounding of a negative inexact division or product (−1/3).
- Whether engine-written values (positions, distances) are truncated or rounded; OpenVirtue rounds
  them to the nearest step, since the original computes them in fixed point to begin with.

## Implementation
`WdlFixed` (`Literal`, `Quantize`, the operators, `Floor`, `Result`), used by `WdlExpression`, the
compound assignments and `ADDT` in `WdlInterpreter`, and the loader's skill `VAL`s and definition
properties. A division by zero throws `WdlExpressionException`; `WdlRuntime` stops that action, logs
it (`LastActionError`) and goes on, instead of shutting down. Tests:
`WdlExpressionTests.Evaluates_TheOriginalsFixedPointArithmetic`,
`UndefinedFunctionResults_ReadAsTheOriginalsValues`, `DivisionByZero_Throws`. `ovtool oracle diff`
matches `values`, `logic`, `sqrt-neg`, `log-zero` and `asin-two`.
