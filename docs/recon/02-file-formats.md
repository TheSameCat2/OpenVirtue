# File Formats to Reverse / Reimplement

Reimplementing the engine = writing **our own** readers for each format below.
Where a published Conitec spec or a permissively documented format exists, cite
it; where it doesn't, reverse-engineer the bytes ourselves (clean-room) and
document the layout here as we learn it.

## `WRS` — Acknex-3 compressed resource archive

The container that ships the game's content. Structure derived from the SaintsX
QuickBMS script (`_research/SaintsX113/extract-wrs.bms`) and **confirmed against
the real retail archives** — implemented and validated in `OpenVirtue.Formats`:

```
big-endian
repeat while offset < fileSize:          // no file header; records start at offset 0
    name     = 13-byte fixed string (NUL-padded filename)
    zsize    = u32  (compressed size)
    size     = u32  (uncompressed size)
    data     = zsize bytes, LZSS-compressed   (comtype = lzss)
    advance offset by zsize
```

- **There is no leading size field.** The QuickBMS recipe's `get asize asize`
  reads the *file-size pseudo-value* (consuming zero bytes); it only bounds the
  loop. (Original recon mis-read this as a 4-byte header — corrected after dumping
  real bytes: the first record begins at offset 0 with `palblack.pcx`, 145 → 961.)
- **Compression: LZSS.** Implemented clean-room from the public-domain algorithm;
  the canonical variant (4096 window, LSB-first flags, etc.) byte-matches the
  game. Every entry in all six retail `.WRS` files decompresses to its exact
  stated size. See `src/OpenVirtue.Formats/PROVENANCE.md`.
- Output members are `WDL`, `WMP`, `PCX`, `WAV`, etc.
- Saints' `WRS` files were noted as "encrypted/packaged"; in practice they are
  plain LZSS — **not encrypted** (confirmed: no circumvention, clear of DMCA §1201).

**Status:** ✅ **Done** — `WrsArchive`/`WrsEntry` enumerate + decompress, validated
against real data. This was the first milestone; everything else builds on it.

## `WDL` — Wad Definition Language (scripts)

Plain-text scripts (after WRS extraction). They define objects, skills, actions,
synonyms, panels, and level wiring. Per-world module set observed from the
SaintsX patch tree (these are the actual source file names):

```
globals, common, media, sounds, menu, move, doors, objects, allobj,
weapon, prayer, scroll, enemies1/2/3, dead, debug, statue, multi,
gallery, mall, lonely, despair, <WORLD>.wdl   (+ per-world *snd.wdl)
```

Grammar reference: firoball's **`WDL2CS`** contains a complete WDL grammar
(AtoCC/Lex-Yacc) — invaluable as a **specification reference** for writing our
own parser, but it is **CC BY-NC**, so read-to-understand only; write our own
grammar. WDL syntax visible in the patch diffs uses `RULE`, `IF_EQUAL`, `SET`,
`IF (...) { }`, skill arithmetic, etc.

**Action:** write our own WDL lexer/parser → AST → interpreter. (We interpret,
we don't transpile — see strategy doc for why.)

## `WMP` — compiled/level map (geometry)

Binary map: REGIONs, WALLs, THINGs, ways, and references to textures/WDL objects.
firoball's **`WMPio`** reads "any version" into A3 map-object classes and writes
the latest — again a **reference** (CC BY-NC) for the binary layout, not code to
copy. Note `WMPio`'s own caveat: a WMP is meaningless without the WDL object
definitions it references, so the **WMP and WDL readers must be developed
together.**

**Action:** reverse the WMP record layout (region/wall/thing tables) against real
Saints maps; cross-check field meanings against `BaseObject`'s property list.

## Asset inventory (measured from real archives)

Using our `OpenVirtue.Tools wrs list`, the actual contents are now known:

| Archive | Entries | PCX | WAV | WDL | WMP | MDL/FLC |
|---------|--------:|----:|----:|----:|----:|--------:|
| `START.WRS`  | 4   | 2   | 0   | 1   | 1 | 0 |
| `apathy.wrs` | 818 | 685 | 109 | 23  | 1 | **0** |

**Key finding: Saints of Virtue is sprite-only.** The biggest gameplay level
contains **no MDL models and no FLC animations** — only PCX images, WAV sounds,
WDL scripts, and a single WMP map. Implications:

- The renderer needs **textured walls/floors/sky + billboard sprites only** —
  **no 3D model pipeline** is required for parity. (Big simplification.)
- Format priority is now: **PCX** (the dominant asset) → WDL → WMP → WAV.
- Each level is one `WMP` map plus its scripts and assets, all in one `WRS`.

(To confirm globally, inventory the remaining levels too; none are expected to
introduce models.)

## `MDL` / `FLC` — models & FLIC animation

**Not used by Saints (confirmed absent from `apathy.wrs`).** Deprioritized; only
revisit if a later level inventory turns one up. If ever needed, the relevant
model generation is MDL3-era and Conitec publicly documents the MDL5/HMP5 formats
(`manual.conitec.net/prog_mdlhmp.htm`).

## `PCX` — textures & sprites *(next reader — M2)*

Standard ZSoft **PCX** (8-bit, RLE, paletted) — 685 of 818 entries in apathy.
Well-documented public format; implement clean-room. Watch for the **256-color
palette block** appended at end-of-file (after a `0x0C` marker), per-file vs.
shared palettes (note the recurring `palblack.pcx`/`palred.pcx` — likely shared
palettes), and sprite/billboard-frame conventions.

**Status:** ✅ **Done** (`OpenVirtue.Formats.Pcx.PcxImage`) — validated against
every PCX in the archives. Observed: **palette index 0 is bright green `(0,255,0)`**,
the likely **sprite color-key** for transparency — the renderer should treat
index 0 as transparent for billboard sprites (confirm per-sprite during rendering).

## `WAV` — sounds

Standard PCM WAV. Trivial.

## `WDF` / `MDF` (e.g. `WWRUN.WDF`, `WWRUN.MDF`)

Engine-level WAD/resource files for the **Windows** runtime (`WVRUN.EXE`) —
likely the built-in fonts, default textures, and shared engine assets. Inspect;
may or may not be needed depending on whether we replicate built-ins ourselves.

## `SMK` — Smacker video (intro/outro)

RAD Game Tools **Smacker**. The format is documented and FFmpeg decodes it; we
can play intros via an FFmpeg-based decoder or skip them (SaintsX leaves them
non-functional). Not on the critical path.

## `FLC` / `FLI` — FLIC animation

Autodesk Animator FLIC (the `Flic` asset type in the API). Well-documented public
format; used for animated textures/cutscenes.

---

### Format milestone order (each unblocks the next)

1. **WRS** (LZSS) → get raw files out.
2. **PCX** + palette → see textures.
3. **WDL** lexer/parser/interpreter → understand objects & logic.
4. **WMP** → load level geometry.
5. **WAV** → audio.
6. **MDL / FLC / SMK** → only if/when content needs them.

> Build a tiny CLI inspector (`tools/`) for each format as we go — dump records to
> text/JSON — so we can diff our parse against the real files and against the
> DOSBox-X oracle.
