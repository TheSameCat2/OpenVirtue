# Prior Art

Three communities have touched this space. None has done what we're doing
(native C#/Direct3D Acknex-3 runtime), but all are valuable references.

## 1. firoball — the Acknex-3 → Unity stack *(closest prior art)*

GitHub: `github.com/firoball` · site: `firoball.de`. A coherent toolchain to run
Acknex-3 games in **Unity**:

| Repo | What it is | License | Use to us |
|------|-----------|---------|-----------|
| **WDL2CS** | WDL → C# **transpiler** (AtoCC/Lex-Yacc grammar) | **CC BY-NC 4.0** | Reference for WDL **grammar/semantics**. Do not copy. |
| **AcknexCSApi** | Hand-written **Acknex-3 C# API** the transpiled code targets | unclear (firoball orig.); fork claims MIT | Reference **spec of the API surface**. (Bundled copy = TheSameCat2 fork.) |
| **WMPio** | Reader/writer for **WMP** map files | **CC BY-NC 4.0** | Reference for **WMP binary layout**. Do not copy. |
| **WRSExtractor** | CLI **WRS** archive extractor | not stated (based on rickomax + morkt's `GameRes.Compression`) | Reference for WRS/LZSS. Check deps' licenses. |
| **WDLTransTest** | Batch test harness for transpiler ↔ API | — | Shows how they validate coverage. |
| **uWED** | Acknex World Editor in Unity | — | Editor reference (later). |
| **BmArrayLoader** | Bitmap-array loader | — | Texture-array reference. |
| **Wm3Util / A3Tools** | A3→A8 level conversion, `.wm3` import to Unity | — | Geometry conversion reference. |

firoball's stated long-term goal is "enabling games created with Acknex-3 to run
with a modern engine like Unity." Several games transpile+run already (Mr. Pibb,
3D Hunting, etc.). **Our divergence:** native Direct3D, no Unity, interpreter not
transpiler, OSI-licensed.

> ⚠️ The **NonCommercial** clause on WDL2CS/WMPio is the central constraint of
> this whole project. See `04-licensing-and-legal.md`.

## 2. rickomax (Ricardo Reis)

GitHub: `rickomax`. Did early **WRS extraction** and Acknex format work (his code
underpins `WRSExtractor`) and Unity importers. "The Varginha Incident" is his
own Acknex-adjacent project. Reference for WRS/asset internals; confirm license
before reuse.

## 3. TheSameCat2 — the bundled fork

GitHub: `TheSameCat2`. The `_research/AcknexCSApi` you provided is **their fork**
of firoball's API (remote: `git@github.com:TheSameCat2/AcknexCSApi.git`). Their
commit log shows API work to make several A3 games transpile/compile:
`Saints of Virtue`, `Hades2`, `office`, `escape`, `OPdemo3`, `VRDemo`. GitHub
lists the fork as **MIT**, but **the local checkout has no `LICENSE` file** and it
derives from firoball's differently-licensed work — so treat its license as
**unsettled** until confirmed in writing.

## 4. SaintsX — the fan patch (bundled, `_research/SaintsX113`)

Site: `saintsofvirtuex.com` · Discord linked in its readme. **Not a
reimplementation** — explicitly "not a remake, remaster, or source port." It:

- binary-patches retail 1.0a/1.1 data up to the 1.3 release (`bsdiff`/`bspatch`),
- extracts the WRS with **QuickBMS**,
- applies WDL `.patch` files (modern controls, mouselook, 60 fps, 800×600,
  physics fixes),
- runs the **DOS `VRUN.EXE` inside DOSBox-X**.

Value to us:

- **Behavioral oracle** — the real engine, runnable, to diff against.
- **Readable WDL diffs** — `patches/*/*-wdl.patch` document the original movement,
  weapon, trap, and entity math (and the deltas to make them frame-rate-correct).
- A **model for our end-user/legal posture**: the author deliberately ships **no
  game assets** out of respect for Shine Studios, even though many call it
  abandonware. We adopt the same stance.

## Comparison

| Effort | Approach | Runtime | Native? | License | Ships game files? |
|--------|----------|---------|---------|---------|-------------------|
| SaintsX | patch + emulate | DOS VRUN in DOSBox-X | no (emulated) | mixed (bundles GPL/zlib tools) | no |
| firoball | transpile + reimplement API | Unity | no (Unity) | CC BY-NC | no |
| **OpenVirtue (us)** | reimplement runtime | native .NET + Direct3D | **yes** | **OSI (TBD, rec. GPLv3)** | **no** |
