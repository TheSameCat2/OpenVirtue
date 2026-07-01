# Provenance — OpenVirtue.Formats

How each format reader in this library was derived, per the clean-room policy in
[ADR-0005](../../docs/adr/0005-clean-room-reference-only-policy.md). **No code here
is ported from firoball's, rickomax's, or any other third-party source.** We
implement from published format/algorithm descriptions and our own byte analysis,
and validate against real game data used only locally (never committed).

## LZSS (`Compression/Lzss.cs`)

- **Source of truth:** the classic, **public-domain LZSS** algorithm (Haruhiko
  Okumura, 1988), a widely-republished reference algorithm. Implemented from its
  published description.
- **Parameters used:** 4096-byte window, max match 18, min match 3
  (THRESHOLD = 2), control bits LSB-first (set bit = literal), match encoded as
  `low offset byte` + `(high offset nibble << 4 | length nibble)`, ring buffer
  pre-filled with `0x20`. These are the canonical defaults and match the codec
  QuickBMS calls `comtype lzss`.
- **Encoder:** generated archives use a deterministic literal-only encoder. This
  produces valid streams without attempting match search, which keeps the writer
  small and auditable for oracle fixtures.
- **Known dialect-variation points** (check these first if real-data validation
  ever disagrees): flag-bit order (LSB vs MSB), literal/match bit polarity,
  length bias (`+THRESHOLD` vs `+THRESHOLD+1`), and the initial dictionary fill
  byte.
- **Validation:** byte-exact agreement is confirmed against real `.WRS` payloads
  (see the guarded integration test); those files are user-supplied and live only
  under the git-ignored `_research/`.

## PCX (`Pcx/PcxImage.cs`, `Pcx/Rgb24.cs`)

- **Source of truth:** the public **ZSoft PCX** format specification — a 128-byte
  header, per-scanline RLE (bytes with the top two bits set are run-count/value
  pairs), and a 256-color palette appended at end-of-file behind a `0x0C` marker.
- **Confirmed against real data:** the game's images are ZSoft v5, 8-bit,
  single-plane, RLE, with the appended palette (e.g. `black.pcx` is 256×256). We
  support exactly that variant and reject others. Every `.pcx` entry across the
  retail archives decodes to a `Width*Height` index buffer + 256-color palette via
  the guarded integration test.

## WDL parser (`Wdl/WdlParser.cs`, `Wdl/WdlItem.cs`)

- **Approach:** a generic syntax tree — each item is `keyword header… ( { block } | ; )`
  or a `name:` label. It captures structure only (declarations, ACTION bodies, nested
  IF blocks, statements all look the same), deferring statement/expression semantics to
  later layers. Derived from the observed language, not from firoball's CC-BY-NC grammar.
- **Confirmed against real data:** every `.wdl` across all six archives parses to a
  balanced tree (guarded integration test).

## WDL preprocessor (`Wdl/WdlPreprocessor.cs`)

- **Behaviour:** flattens a level's main script by resolving `INCLUDE`s (recursively,
  include-once) and evaluating nested `IFDEF`/`IFELSE`/`ENDIF` + `DEFINE`. Derived from
  the game's own resolution blocks (e.g. APATHY.WDL defaults to defining `HIRES`).
- **Confirmed against real data:** flattening real levels resolves their `INCLUDE`s
  (apathy's 21 → 400+ skills) with the expected default symbols.

## WDL lexer (`Wdl/WdlLexer.cs`, `Wdl/WdlToken.cs`)

- **Source of truth:** the language's surface syntax, observed from the game's own
  scripts and the SaintsX patch diffs (a *description of syntax*, not third-party
  code). firoball's WDL2CS grammar is **CC BY-NC and was not consulted or copied**.
- **Syntax facts** (discovered while validating against real scripts): `//` `#`
  line comments and `/* */` block comments; multi-line string literals with no
  escape processing; `.` member access; `:` labels; `<file>` references vs. `<`/`>`
  comparison operators (disambiguated by look-ahead); `[ ] @ ? ' \` appear only in
  strings/comments.
- **Confirmed against real data:** every `.wdl` across all six archives tokenizes
  to end-of-file via the guarded integration test (real files local-only).

## WMP map (`Wmp/WmpMap.cs`, `Wmp/WmpTypes.cs`)

- **Source of truth:** the game's own `.WMP` files, which are **text** emitted by
  WED (a self-describing format with column-header comments). We derived the record
  grammar by reading the maps directly — not from firoball's `WMPio` (CC-BY-NC, not
  consulted).
- **Records** (whitespace-delimited, `;`-terminated, `#` comments): `VERTEX x y z`;
  `REGION name floor ceil`; `WALL name v1 v2 r1 r2 offsx offsy`; `THING`/`ACTOR
  name x y angle region`; `PLAYER_START x y angle region`. Confirmed to be the full
  set across all six maps.
- **Confirmed against real data:** all six maps parse; apathy's record counts are
  pinned and every wall references valid vertices (guarded integration test).

## WAV (`Wav/WavSound.cs`)

- **Source of truth:** the public Microsoft **RIFF/WAVE** container spec — a
  `RIFF`…`WAVE` wrapper with a `fmt ` chunk and a `data` chunk; chunks are padded to
  even byte boundaries.
- **Confirmed against real data:** all of the game's sounds are uncompressed PCM
  (format 1; mostly 8-bit mono 11025 Hz). Every `.wav` in the archives decodes via
  the guarded integration test.

## WRS archive (`Wrs/WrsArchive.cs`, `Wrs/WrsEntry.cs`, `Wrs/WrsFile.cs`)

- **Source of truth:** the WRS record structure — fixed records of `name[13]` +
  `u32 compressedSize` + `u32 uncompressedSize` + LZSS payload, running from offset
  0 to end of file (**no file header**). Derived from the public QuickBMS
  extraction recipe (a *description of bytes*, not third-party code) and confirmed
  by dumping real archives. See
  [docs/recon/02-file-formats.md](../../docs/recon/02-file-formats.md).
- **Confirmed against real data:** every entry of all six retail `.WRS` files
  (apathy, heart, legalism, newage, start, title) parses and decompresses to its
  exact stated uncompressed size — which also validates the LZSS dialect above.
  Verified via the guarded integration test (real files are local-only under the
  git-ignored `_research/`; never committed).
- **Writer:** emits the same fixed records with big-endian size fields and
  literal-only LZSS payloads. It is intended for generated synthetic fixtures and
  local tooling, not for redistributing game data.
