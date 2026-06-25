# File Formats to Reverse / Reimplement

Reimplementing the engine = writing **our own** readers for each format below.
Where a published Conitec spec or a permissively documented format exists, cite
it; where it doesn't, reverse-engineer the bytes ourselves (clean-room) and
document the layout here as we learn it.

## `WRS` — Acknex-3 compressed resource archive

The container that ships the game's content. From the SaintsX QuickBMS script
(`_research/SaintsX113/extract-wrs.bms`) the structure is fully known:

```
big-endian
asize        = total archive size (u32)
repeat until offset >= asize:
    name     = 13-byte fixed string (filename)
    zsize    = u32  (compressed size)
    size     = u32  (uncompressed size)
    data     = zsize bytes, LZSS-compressed   (comtype = lzss)
    advance to next record
```

- **Compression: LZSS.** We must implement an LZSS decompressor matching
  Acknex's window/flag conventions. (QuickBMS's `comtype lzss` is the reference
  behavior to match; rickomax/morkt's `GameRes.Compression` is a code reference
  but check its license before borrowing.)
- Output members are `WDL`, `WMP`, `PCX`, `WAV`, etc.
- Saints' `WRS` files were noted as "encrypted/packaged"; in practice the
  QuickBMS script decompresses them with plain LZSS, so likely just compressed,
  not encrypted. Verify on real bytes.

**Action:** implement `WrsArchive` reader (enumerate + decompress) as the very
first milestone — everything else depends on getting bytes out.

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

## `MDL` — model format

Saints (A3) primarily uses sprite billboards, but MDL models may appear. The
relevant generation is **MDL3** (A4 uses MDL3; A5 uses MDL4/5). Conitec
**publicly documents MDL5/HMP5** and the older formats
(`manual.conitec.net/prog_mdlhmp.htm`). MDL is Quake-`.mdl`-derived (vertex frame
animation). Implement only if real Saints content uses it (confirm during asset
extraction).

## `PCX` — textures & sprites

Standard ZSoft **PCX** (8-bit, RLE, paletted). Well-documented public format;
trivial to implement ourselves. Watch for a **shared global palette** vs.
per-file palettes, and for sprite-sheet / billboard-frame conventions.

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
