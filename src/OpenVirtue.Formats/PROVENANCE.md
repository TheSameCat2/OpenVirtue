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
- **Known dialect-variation points** (check these first if real-data validation
  ever disagrees): flag-bit order (LSB vs MSB), literal/match bit polarity,
  length bias (`+THRESHOLD` vs `+THRESHOLD+1`), and the initial dictionary fill
  byte.
- **Validation:** byte-exact agreement is confirmed against real `.WRS` payloads
  (see the guarded integration test); those files are user-supplied and live only
  under the git-ignored `_research/`.

## WRS archive (`Wrs/WrsArchive.cs`, `Wrs/WrsEntry.cs`)

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
