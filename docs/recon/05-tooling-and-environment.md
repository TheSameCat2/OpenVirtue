# Tooling & Environment

## Already present on this machine (verified 2026-06-25)

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | **10.0.301** (+ 6.0, 8.0) | Latest; target **net10.0** / **net10.0-windows**. |
| git | 2.54.0 | OK. |
| winget | 1.29 | Use for the installs below. |
| DOSBox-X | bundled in `_research/SaintsX113` | Our behavioral **oracle**. |
| QuickBMS | bundled in `_research/SaintsX113` | WRS extraction (reference behavior; don't redistribute). |

## Recommended installs (engine development)

### Core
- **Visual Studio 2022** (Community) with the **.NET desktop** + **Game
  development with C++**/**DirectX** workloads — best C# + graphics debugging,
  HLSL tooling, and **PIX**/Graphics Diagnostics integration.
  `winget install Microsoft.VisualStudio.2022.Community`
  (Or **VS Code** + **C# Dev Kit** if you prefer lighter-weight.)
- **PIX on Windows** — GPU capture/debugging for Direct3D 12 (and useful for 11).
  `winget install Microsoft.PIX`

### NuGet packages (add when `src/` starts)
- **Vortice.Windows** — modern .NET bindings for **Direct3D 11/12**, DXGI,
  Direct2D, DirectWrite, **XAudio2**, **XInput**, **DirectInput**. MIT-licensed,
  actively maintained, the de-facto choice for native DirectX in C#. This is our
  rendering/audio/input foundation. (`Vortice.Direct3D11`, `Vortice.DXGI`,
  `Vortice.D3DCompiler`, `Vortice.XAudio2`, `Vortice.XInput`.)
- **Silk.NET.Windowing** *(optional)* — windowing/input abstraction if we don't
  hand-roll Win32. (Silk's GL/Vulkan bindings are good; for **DirectX** prefer
  Vortice.) Alternatively use raw **Win32** via `TerraFX.Interop.Windows` or
  P/Invoke for the HWND + message loop.
- **FFmpeg** binding (e.g. `FFmpeg.AutoGen`) **only if** we play Smacker intros;
  otherwise skip.

> **Why Vortice over MonoGame/Unity:** you asked for *native Windows best
> practices and DirectX*. Vortice is a thin, faithful binding to the actual
> DirectX APIs — exactly the "lean on native tech" posture you want — without an
> opinionated engine layer in the way. MonoGame would abstract DirectX away;
> Unity is what the *other* prior-art project already uses and isn't "native."
> See the ADR we'll write to record this decision.

### Reverse-engineering / inspection (for `_research` study only)
- **ImHex** or **010 Editor** — hex editors with binary templates; ideal for
  reversing WMP/MDL record layouts. `winget install WerWolv.ImHex`
- **Ghidra** (free, NSA) — *only* if we ever need to study `VRUN.EXE` behavior.
  **Clean-room caveat:** whoever runs a disassembler must not also write engine
  code (see legal doc §2a, §4). `winget install GhidraSoftwareReverseEngineeringFramework.Ghidra`
  (or download from ghidra-sre.org). Largely avoidable if format docs + the
  DOSBox-X oracle suffice — prefer the black-box route.
- **QuickBMS** — already bundled; the `extract-wrs.bms` script documents the WRS
  layout we'll reimplement.

### Quality / CI (add early, cheap to maintain)
- **xUnit** (or NUnit) for tests; **FluentAssertions** optional.
- **dotnet format** + the `.editorconfig` in repo root for style.
- **GitHub Actions** `dotnet build`/`test` workflow once `src/` exists.

## Suggested target frameworks

- Engine + tools: `net10.0` (cross-compilable parts) and `net10.0-windows` for
  the Direct3D/Win32 host.
- Prefer **x64** (and consider **ARM64** later — Vortice supports it).

## What we deliberately do **not** install/redistribute

- We use DOSBox-X / QuickBMS **locally** from `_research/` as tools and oracles;
  we never bundle them or game data into our repo or releases.
- No Conitec SDK is required (we don't build *with* 3D GameStudio; we replace it).
