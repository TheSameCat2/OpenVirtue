# OV-20261104 — The string instructions: TO_STRING, SET_STRING, ADD_STRING, SET_SKILL

Status: confirmed (three carriers) and implemented.

## Question
The retail scripts build their winner screen with `TO_STRING` and `ADD_STRING` and pick save
names with `SET_STRING`. The manual (pp. 85–86) says `TO_STRING` copies "the digit
representation" of a skill, that a copy never exceeds the first string's original length, and
that `SET_SKILL` reads a string "if it contains a number". How are numbers written, where does a
copy stop, and what does `SET_SKILL` make of texts that are not quite numbers?

## Method
- Oracle: native Linux DOSBox-X 2026.10.01 with `SDL_VIDEODRIVER=dummy`, `forcerate=60`, the
  SaintsX-patched `VRUN.EXE` V3.963.
- Fixture generator: `ovtool oracle prepare strings <dir>` (`tools/OpenVirtue.Tools/StringProbeCases.cs`).
  Each line is a token and the string's content up to a `|` (format, concat) or the skill's
  value (parse). Traces (`OVSTR0.TXT`, sha256 first 8): format `8734b39d`, concat
  `012c6877`, parse `fad1b97d`.
- Confirmation that no proprietary code/assets are committed: synthetic level only; nothing under
  `_research/` is committed.

## Readings
| Carrier | Reading |
|---|---|
| format | `TO_STRING` of 0, 1, 12, 100000, 1000000, −1: `0`, `1`, `12`, `100000`, `1000000`, `-1`; of 1.5, −2.25, 0.001, −0.5, 0.5, 2.5: `1.500`, `-2.250`, `0.001`, `-0.500`, `0.500`, `2.500`; of 1234.5678, 65535.999, 0.333, 7.999, 3.14159, −1000.125: `1234.567`, `65535.998`, `0.332`, `7.998`, `3.141`, `-1000.125`; of the literal 2.5 `2.500`, of `TICKS` `13.406`; into a 2-character string, 1234.5 gives `123` and 7 gives `7` |
| concat | strings of 10, 2, 6, 0 and 1 characters: `SET_STRING` of the 6 into the 2 gives `CDE`; of the empty into it, nothing; `ADD_STRING` of the 6 to the emptied 2, `CDE`; the 10 set to `CDE` and the 6 added twice: `CDECDEFGH` then `CDECDEFGHCD`, a third add of `Z` nothing; `Z` into the empty string: `Z`, added again nothing; the empty's `Z` into the 1: `Z` |
| parse | `SET_SKILL` from `12.5` 12.5, `abc` 0, ` 7` 7, `1e3` 1000, `-3.25` −3.25, `0x10` 0, `12abc` 12, the empty and blank texts 0, `+4` 4, `.5` 0.5, `1,5` 1, `65536` 65536.000, `3.14159265` 3.141, `7.` 7, `-` 0 (the skill at 99 before each) |

- **`TO_STRING` writes a whole 22.10 value without decimals and any other with three, rounded
  as `PRINT_VALUE` prints it** (1234.5678 is held as 1234.5674 and written `1234.567`;
  3.14159 as 3.140625, written `3.141`).
- **A copy takes one character more than the string's declared length**: `SET_STRING`,
  `ADD_STRING` and `TO_STRING` alike (the manual's "original length" is the length plus the
  terminator's place). `INKEY` stops at the declared length (OV-20261029-inkey).
- **`SET_SKILL` reads the number the text starts with**, after blanks, with a sign, decimals
  and an exponent, cut to 22.10 (3.140625 for `3.14159265`), and 0 for a text that starts with
  none; 65536 is kept as read (printed `65536.000`).

## Implementation
`WdlString.WithCopiedValue` (the declared length plus one; `WdlRuntime.SetString`),
`WdlInterpreter.ExecuteToString` (whole values without decimals, `FormatPrintValue` otherwise),
`WdlInterpreter.LeadingNumber` (`ExecuteSetSkill`). Test
`WdlRuntimeTests.Runtime_StringInstructions_CopyAppendConvertAndParse`. concat and parse match in
full; format to the `TICKS` of a fast launch's start-up (the clock family).
