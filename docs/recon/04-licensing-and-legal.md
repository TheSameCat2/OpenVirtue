# Licensing, Legal & Ethical Notes

> **Not legal advice.** I'm an engineering assistant, not a lawyer. For anything
> you intend to publish, get a real IP attorney to review — especially the
> reverse-engineering and trademark questions. This document is a risk map so you
> know *what* to ask about.

## 1. The thing that is clearly fine: a clean-room engine

Reimplementing a game **engine** so that legally-owned data files run on modern
systems is a well-established, defensible practice (OpenMW for Morrowind,
ScummVM, OpenRA, devilutionX, etc.). The legal pillars:

- **Game data is copyrighted; the engine you write is your own.** We ship *only*
  our original code. The user supplies their own game files. We distribute **no**
  WRS/WDL/WMP/MDL/PCX/WAV, no `VRUN`/`WVRUN`, no Conitec WDF/MDF built-ins.
- **APIs, formats, and behaviors are not, by themselves, copyrightable.**
  Reproducing *what* the Acknex-3 runtime does (its object model, commands,
  function names, tick semantics) is reimplementation, not copying — consistent
  with the reasoning in *Google v. Oracle* (US). The `AcknexCSApi` is useful to
  us precisely because an **API surface is a spec, not protected expression** —
  but see the clean-room rules below for *how* to use it safely.

## 2. The things to be careful about

### 2a. Do NOT decompile the original executables and copy code
Disassembling `VRUN.EXE`/`WVRUN.EXE` to *understand* behavior is a gray area;
**copying** its code (even via a decompiler's output) into our engine is
infringement. Our policy: **black-box** the original (observe inputs/outputs,
read published docs, read our own format dumps). If anyone ever disassembles for
study, that person must **not** also write engine code from it.

### 2b. Reverse-engineering file formats & the game EULA
RE of formats *for interoperability* enjoys some legal protection (US case law;
EU Software Directive Art. 6 decompilation-for-interop), **but** the original
game's **EULA may forbid reverse engineering**. Action items:
- **Locate and read the Saints of Virtue EULA / license** (CD insert, installer,
  `install.dat`). Note any anti-RE or anti-derivative clauses.
- There is **no DRM/encryption circumvention** apparent (WRS is LZSS-compressed,
  not encrypted), which keeps us clear of DMCA §1201 anti-circumvention — **but
  verify** on the real bytes before asserting this publicly.

### 2c. Conitec / Acknex / 3D GameStudio IP
The engine is **Conitec Datensysteme's** intellectual property. We may
reimplement its *behavior*; we may **not** ship its runtime, its bundled assets
(fonts, default textures in WDF/MDF), or its name as our product name. 3D
GameStudio shipped commercial games for decades — Conitec is a live rights
holder, not defunct.

### 2d. Trademarks
"**Saints of Virtue**", "**3D GameStudio**", "**Acknex**" are trademarks of their
owners. Use them **nominatively** only — *"an open engine compatible with Saints
of Virtue"* — never as the project's product name, logo, or in a way implying
endorsement. Pick an original project name (the README's "OpenVirtue" is a
placeholder — confirm it's not confusingly similar / not someone's mark).

## 3. The CC BY-NC problem (most actionable constraint)

The best existing references are **license-incompatible with an open project**:

| Reference | License | Can we copy code into our distributed engine? |
|-----------|---------|----------------------------------------------|
| firoball **WDL2CS** | **CC BY-NC 4.0** | ❌ No — NonCommercial, and CC-on-code, incompatible with GPL/MIT |
| firoball **WMPio** | **CC BY-NC 4.0** | ❌ No |
| firoball **AcknexCSApi** (orig.) | unclear | ❌ Treat as "all rights reserved" until confirmed |
| **AcknexCSApi** (TheSameCat2 fork) | GitHub says MIT, **no local LICENSE**, derived work | ⚠️ Unsettled — don't rely on it |
| **WRSExtractor** + `GameRes.Compression` (morkt), rickomax utils | not stated / mixed | ⚠️ Check each dependency |

Why CC BY-NC is fatal to copying:
- **NonCommercial** bars commercial use; an OSI "open source" license **cannot**
  add that restriction, so NC code can't live in an MIT/GPL project.
- Creative Commons itself **recommends against using CC licenses for software**.
- NC is incompatible with **both** GPL and permissive licenses.

**Therefore:** treat WDL2CS / WMPio / AcknexCSApi as **read-only references**
under a clean-room process (Section 4), **or** — much better — **email firoball
and TheSameCat2** and ask them to **dual-license / relicense** the API spec and
grammar under a permissive license (MIT/Apache) or grant you a written exception.
Given the shared preservation goal, this is plausibly a quick yes and would save
months. Get any permission **in writing**.

## 4. Clean-room methodology we will follow

To keep provenance clean and defensible:

1. **Reference material stays out of the repo.** `_research/` is git-ignored;
   nothing derived from it is committed verbatim.
2. **Specs, not source.** From references we extract *facts* — "WRS = LZSS with a
   13-byte name + u32 zsize + u32 size header", "ACTOR has an `if_arrived`
   hook" — and write those facts into `docs/` in our own words. We then implement
   from `docs/`, not from the reference code on screen.
3. **No copy-paste, ever**, from CC-BY-NC / unlicensed / decompiled sources.
4. **Attribute the ideas** (these docs do) without importing the expression.
5. If the project grows, consider a **two-person split** (one reads references &
   writes specs; another writes code only from specs) for the strongest record.
6. Keep a `PROVENANCE.md` per nontrivial format reader noting *how* we learned the
   layout (published doc URL, our own byte analysis, behavioral test).

ADR-0007 formalizes the next layer of this process: hidden engine invariants are
learned through sanitized oracle observation records, not copied expression. See
[`../adr/0007-oracle-based-invariant-discovery.md`](../adr/0007-oracle-based-invariant-discovery.md)
and [`../clean-room/oracle-protocol.md`](../clean-room/oracle-protocol.md).

## 5. Our own license (open decision — recommend GPLv3)

- **GPLv3** — the **OpenMW model**. Keeps the engine and derivatives open;
  strong fit for a preservation project; compatible with our planned deps
  (.NET runtime = MIT, Vortice.Windows = MIT, both GPL-compatible).
- **MIT / Apache-2.0** — maximally permissive; easier for contributors and reuse;
  Apache-2.0 adds an explicit patent grant. Choose if you want the code reused
  freely (including commercially).
- Whatever you pick, it must **not** be polluted by CC-BY-NC code — another reason
  to keep those at arm's length.

No `LICENSE` is committed yet; this is yours to decide.

## 6. Ethical obligations (you asked — here they are)

- **Credit prominently and generously.** Shine Studios, Cactus Game Design,
  Conitec, and the community researchers (firoball, rickomax, TheSameCat2,
  SaintsX author). Done in `README.md`; keep it current.
- **Ship no assets; require the user's own copy.** This is both legal hygiene and
  respect for the creators (the SaintsX author's stated stance — match it).
- **Don't monetize off others' work** in a way that exploits the original
  authors. A donation link for *your* effort is one thing; selling the game or its
  assets is not okay.
- **Consider reaching out to the rights holders for a blessing.** Cactus Game
  Design appears to still exist; a short, respectful note explaining the
  preservation goal can convert legal risk into goodwill — and they may even
  share materials. This is optional legally but strong ethically and reputationally.
- **Respect the work's nature.** It's a sincere project by a small studio; frame
  the reimplementation as preservation and appreciation, as you intend.

## 7. Open legal to-dos

- [x] **Pick the engine license** → **GPL-3.0-or-later** ([ADR-0003](../adr/0003-license-gplv3.md), `/LICENSE`).
- [x] **Decide how to use prior art** → **reference-only, clean-room**
  ([ADR-0005](../adr/0005-clean-room-reference-only-policy.md)). The project owner
  is **TheSameCat2** (maintainer of the bundled AcknexCSApi fork); firoball's
  upstream appears **abandoned**, so relicensing-by-request is not pursued. We
  implement everything clean rather than depend on CC-BY-NC code.
- [x] **Define how to discover hidden runtime behavior** → **oracle-based invariant
  discovery** ([ADR-0007](../adr/0007-oracle-based-invariant-discovery.md),
  [`docs/clean-room/`](../clean-room/)).
- [ ] Read the original game EULA; record any anti-RE/derivative terms.
- [ ] Confirm WRS has no encryption/DRM (keeps us clear of DMCA §1201) — verify on real bytes.
- [ ] Clear the **OpenVirtue** name against existing trademarks before public release
  (GitHub org / domain / package id).
- [ ] (Optional) Contact Shine Studios / Cactus Game Design for a blessing.
- [ ] Have an IP attorney review before public release.
