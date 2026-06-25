# ADR-0001: Renderer — Direct3D 11 via Vortice.Windows
Date: 2026-06-25
Status: accepted

## Context
OpenVirtue must render Acknex-3's 2.5D portal/sector world natively on modern
Windows, leaning on native Microsoft graphics technology (an explicit project
goal). The renderer choice also fixes our audio/input bindings.

## Decision
Use **Direct3D 11** through **Vortice.Windows** (MIT-licensed .NET bindings),
with **XAudio2** for audio and **XInput/DirectInput** for input from the same
binding set. Host via a Win32 window + message loop.

## Alternatives considered
- **Direct3D 12 (Vortice):** more native/explicit, but heavier API surface that
  slows parity work; revisit for the enhancement phase if needed.
- **MonoGame:** abstracts DirectX away — conflicts with the "native DirectX" goal.
- **Unity:** what the existing firoball prior art uses; not native, not our path.
- **Silk.NET:** strong for GL/Vulkan, weaker DirectX bindings than Vortice.

## Consequences
- Fast path to parity; faithful, thin access to the real DirectX APIs.
- D3D11 feature level is ample for a 2.5D paletted renderer.
- Migration to D3D12 later is possible but not assumed; record as a new ADR if so.
