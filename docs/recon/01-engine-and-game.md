# The Game & the Acknex-3 Engine

## The game

| | |
|---|---|
| Title | Saints of Virtue |
| Developer | Shine Studios |
| Publisher | Cactus Game Design |
| Released | July 1999 (Christian bookstores); conceived Oct 1997, ~16 months dev |
| Genre | First-person shooter (allegorical/Christian themed) |
| Engine | **Conitec Acknex-3** ("A3"; later marketed as *3D GameStudio*) |
| Retail versions | 1.0a, 1.1, 1.3 (final, 2004), 1.4 |
| Min spec (per readme) | Pentium, Win95/98/ME or XP SP1, DirectX 5.0+, 64 MB RAM |

The retail install (`_research/Saints-of-Virtue_Win_EN/Saints of Virtue/`) contains:

- **Level/data archives** (`*.WRS`): `TITLE`, `APATHY`, `LEGALISM`, `NEWAGE`,
  `HEART`, `START` — each a compressed bundle of WDL scripts + assets for one
  "world." Saints' worlds map to the seven deadly sins / spiritual themes.
- **Runtimes:** `VRUN.EXE` (DOS), `WVRUN.EXE` (Windows/DirectX), plus
  `WWRUN.WDF` / `WWRUN.MDF` (engine WAD/resource files for the Windows runtime).
- **Video:** `intro.smk` (Smacker), played via `Smackply.exe` / `Smackplw.exe`.
- DirectX 9.0b redistributable, MFC/CRT DLLs, InstallShield uninstaller.

## Engine lineage (so we model the right thing)

Conitec's engine evolved through clearly different rendering architectures:

| Gen | Era | Rendering model |
|-----|-----|-----------------|
| ACK-3D / ACKNEX | early | Wolfenstein-style raycaster |
| **ACKNEX-2 / ACKNEX-3 (A3)** | mid-late 90s | **Doom/Build-style 2.5D portal + sector** — *this game* |
| ACKNEX-4 (A4) | 1999–2000 | first **true 3D** (BSP, MDL models) |
| A5–A8 | 2000s | true 3D, shaders, Lite-C |

**Saints of Virtue is A3.** Practical consequences for us:

- The world is a set of **REGIONs** (sectors, each with a floor and ceiling
  height and textures) joined by **WALLs** (segments with a `left`/`right`
  region — a portal when both sides exist). This is a 2.5D world: no
  room-over-room true 3D geometry.
- Dynamic content is **THINGs** (static billboard sprites) and **ACTORs**
  (animated sprites/AI). Models may be **MDL** (3D) but most A3 content is sprite
  billboards drawn into the 2.5D scene.
- **WAYs** are waypoint paths actors follow.
- Gameplay is driven by **WDL scripts** interpreted at runtime by VRUN/WVRUN.

## The WDL object & execution model (reconstructed from the C# API)

The bundled `AcknexCSApi` is effectively a **machine-readable spec of the
Acknex-3 API surface** — every object type, property, flag, and command the WDL
runtime exposes. Key types (under `_research/AcknexCSApi/Acknex3Api/`):

- **Core**
  - `Skill` — an Acknex *skill*: a bounded numeric variable (val/min/max) with
    `Player`/`Local`/`Global` scope. Skills are the engine's primary state cells.
  - `Var` — scalar value type with engine semantics (`Accel`, `Randomize`, fixed
    behaviors). Math is in `MathV`.
  - `A3Object` / `A3Flags` — base object + the global flag bitset
    (`invisible`, `passable`, `transparent`, `actor`, `thing`, …).
  - `Function` / `Events` / `Scheduler` — the **action/tick model**: objects have
    hook functions (`each_cycle`, `each_tick`, `if_near`, `if_far`, `if_hit`,
    `if_arrived`) the scheduler fires. This is the heart of the runtime loop.
  - `Commands`, `Globals`, `Media`, `Palette`, `Diag`, `Environment`
    (`level`, `map`, `save`, `load`, `screenshot`, `freeze`, …).
- **Map** (`BaseObject` is the shared base — read it; it enumerates the whole
  property set): `Region`, `Wall`, `Thing`, `Actor`, `Way`, `Texture`, `Player`,
  `MapObject`.
- **Asset**: `Bitmap`/`Bmap`, `Flic` (FLC animation), `Font`, `Model` (MDL),
  `Music`, `Ovly` (overlay), `Sound`.
- **UI**: `Panel`, `Window`, `View`, `Button`, `Bar`/`HBar`/`VBar`,
  `Slider`/`HSlider`/`VSlider`, `Digits`, `Picture`, `Text` — the HUD / menu
  widget system (Saints' menus, inventory, scroll reader, 2-D map).

> **Use this API surface as a *checklist of behaviors to reimplement*, derived
> via clean-room reading — not as code to copy.** See the legal doc.

## Behavior notes that will bite us (known A3/Saints quirks)

- **Frame-rate-coupled physics.** Original logic assumes a fixed tick (~16 fps in
  places; `TIME_CORR = 1` ≙ 16 fps per the SaintsX debug panel). Jump height,
  weapon fire rate, traps, and enemy movement are tied to frame time. SaintsX
  spends much of its patch set re-deriving these for 60 fps. **Our engine must
  reproduce the original fixed-tick semantics first**, then optionally decouple.
- **Palette / 8-bit color.** A3 is paletted (256-color, PCX textures). Expect a
  global palette + per-texture indexing; lighting is palette-shaded.
- **Tilt/roll on level start, walk-through-walls "fix" key (`F`)** — collision is
  approximate; the original exposes a manual un-stick. We should match, not
  "improve," during parity.
- **Save format** is engine-defined (`Environment.Save/Load`) — reverse it for
  save compatibility, or define our own and not claim retail-save compat.

## The DOSBox-X oracle

`_research/SaintsX113` runs the DOS `VRUN.EXE` under DOSBox-X at a forced 60 fps,
800×600. Because it is the *actual original engine*, it is our **ground-truth
oracle**: for any ambiguous behavior, we can run the same input there and compare.
The SaintsX `patches/*/**.wdl.patch` files are unified diffs of the *decompressed*
WDL and are directly human-readable — e.g. `move-wdl.patch` shows the exact
mouselook/jump/tilt RULE changes, which double as documentation of the original
movement math.
