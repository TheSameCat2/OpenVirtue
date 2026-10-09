# ADR-0008: Cross-platform host — SDL3 + OpenGL 3.3, superseding Direct3D 11
Date: 2026-10-09
Status: proposed (supersedes ADR-0001 on acceptance)

## Context

ADR-0001 chose Direct3D 11 via Vortice.Windows because "leaning on native Microsoft
graphics technology" was an explicit project goal. That goal has changed: **OpenVirtue
must run on Linux and Windows.** With the rationale gone, the decision has to be remade
rather than quietly patched.

What the port actually touches is narrow. `OpenVirtue.Formats` and `OpenVirtue.Engine`
have **no platform dependencies** (verified 2026-10-09: no Vortice, NAudio, System.Drawing,
WinForms or P/Invoke outside `App`). Every platform binding lives in one file,
`src/OpenVirtue.App/LevelWindow.cs` (≈3,060 lines), which combines:

- a WinForms window and message loop, keyboard/mouse polling and hook dispatch;
- a Direct3D 11 renderer (swap chain, depth buffer, ~30-line HLSL shader, texture upload,
  world/sprite/UI/sky/FLIC draw paths, back-buffer screenshot);
- an eight-voice NAudio mixer with per-voice volume, pan and distance attenuation;
- host servicing of every `IWdl*Context` request (sound, palette, presentation, persistence,
  input, control) and the player-state bridge;
- `System.Drawing` for PNG screenshots.

`OpenVirtue.App` targets `net10.0-windows`; CI runs on `windows-latest` only.

What the renderer needs is modest and fixed by the parity goal: textured quads with a depth
buffer, alpha-test cutout for palette index 0, **indexed-palette shading** (the original is
an 8-bit software renderer whose lighting is a palette/shade-table lookup — "exact
indexed-palette shading" is an open parity item), full-screen FLIC blits, screen-space UI
quads, and screenshots. Performance is not a constraint; determinism and testability are.

## Decision

1. **Windowing, input, timing and audio output: SDL3**, through the `ppy.SDL3-CS` NuGet
   package (MIT; bundles native SDL3 for linux-x64/arm64, win-x64/x86/arm64, osx; net8.0+).
   SDL2 is in maintenance mode; starting on SDL3 avoids a second migration.

2. **Graphics: OpenGL 3.3 core profile**, through `Silk.NET.OpenGL` (MIT, 2.x line). The GL
   context is created by SDL (`SDL_GL_CreateContext`) and Silk loads entry points through
   `SDL_GL_GetProcAddress`, so there is one window/context owner. GL 3.3 is available on
   every Linux and Windows GPU/driver we care about, including Mesa's software rasterizer
   (llvmpipe) for headless CI rendering.

3. **Palette-faithful rendering is the design, not a toggle.** World textures are uploaded as
   8-bit index textures (`R8UI`); the fragment shader looks up a palette LUT and a shade
   table so lighting reproduces the original's table lookups. Enhancements (true-color,
   filtering, higher internal resolution) come later behind toggles, per the parity-first
   philosophy. This is the main reason a hardware renderer is kept rather than switching to
   a software rasterizer: it gives the same lookups *and* keeps the enhancement path open.

4. **Audio: our own eight-voice software mixer feeding an SDL3 audio stream**
   (`SDL_OpenAudioDeviceStream`, float32). The host already computes per-voice volume/pan;
   the original was eight-channel software mixing, so owning the mix is the parity-correct
   choice and removes a native dependency.

5. **Screenshots: a ~60-line PNG writer over `System.IO.Compression`** (zlib). No imaging
   library; `System.Drawing` is Windows-only on .NET.

6. **Project layout** — the port is also the split of the god class:

   ```
   OpenVirtue.Formats   (net10.0)  unchanged
   OpenVirtue.Engine    (net10.0)  unchanged
   OpenVirtue.Host      (net10.0, → Engine)   NEW, no native deps, unit-testable
       RuntimeHost        services IWdl*Context requests; owns the frame loop ordering
       PlayerBridge       debug player ⇄ PLAYER_* skills
       AudioMixer         8 voices → float buffer (pure C#)
       SaveStore / PrintDirectory / PngWriter
       Abstractions:  IPlatformWindow, IInputSource, IAudioSink, IRenderBackend
   OpenVirtue.App       (net10.0, Exe, → Host)  SDL3 + GL implementations + Program
       Platform/Sdl*      window, events, relative mouse, audio device
       Rendering/Gl*      renderer, texture cache, world/sprite/UI/sky/FLIC batches, GLSL
   OpenVirtue.Tools     (net10.0)  unchanged; `oracle prepare` also emits .sh launchers
   ```

   `Host` carries the logic that is currently interleaved with D3D calls and must be
   testable with fakes; `App` becomes thin. `net10.0-windows` and WinForms disappear.

7. **Migration uses the old renderer as a regression oracle.** The D3D11 `LevelWindow` moves
   unchanged into a Windows-only `src/legacy/OpenVirtue.App.D3D11` project (built only on
   Windows via an MSBuild condition, excluded from the Linux solution filter). The retail
   rendered smoke and a set of fixed-camera screenshots are captured from it; the GL renderer
   is driven to match them (image diff with a tolerance for rasterization differences). This
   guards against *regressions during the port only*: the D3D11 renderer was never validated
   against the original (until 2026-10-09 its projection treated `PLAYER_ARC` as a vertical
   FOV in radians and presented 320x200 with square pixels). Parity of the renderer itself is
   measured against DOSBox-X screenshots; the three projection probes (FOV, pixel aspect, tilt
   model) were resolved on 2026-10-09 (OV-20261009-*); the D3D11 renderer consumes
   `OpenVirtue.Engine.Rendering.ProjectionModel` today and the GL renderer must consume the
   same type. When
   the GL path matches the legacy renderer on all six archives and the title→Apathy flow
   (with the corrected projection applied to both, or the diff restricted to non-projection
   layers), the legacy project is deleted in a single commit. It is never extended.

8. **CI** becomes a matrix of `ubuntu-latest` and `windows-latest`: restore, build
   `-warnaserror`, test. Golden-image tests run where a GL context can be created
   (`SDL_VIDEODRIVER=offscreen` or Xvfb + llvmpipe on Linux) and skip with a clear message
   otherwise, mirroring how the guarded real-data tests already behave.

## Alternatives considered

- **SDL2 (SDL2-CS).** Mature bindings, but SDL2 is maintenance-only and SDL3's audio stream
  API is a better fit for a custom mixer. Rejected to avoid migrating twice.
- **SDL_GPU (SDL3's D3D12/Vulkan/Metal abstraction).** Clean API, but shaders must be
  precompiled per backend (SDL_shadercross toolchain) and the C# surface is young. Too much
  tooling for textured quads. Revisit for the enhancement phase if GL ever becomes a limit.
- **Vulkan.** Weeks of plumbing to draw Doom-era sectors; no parity or portability benefit
  over GL 3.3 for this workload.
- **Keep D3D11 on Windows, add GL on Linux.** Two renderers to keep in parity forever, for
  a renderer whose whole job is reproducing palette lookups. Rejected.
- **Software rasterizer to an 8-bit framebuffer, blitted via SDL.** The most faithful option
  and worth remembering: if palette-LUT shading in GLSL cannot match some lookup the
  original does, a software path for the *world* layer is the fallback. Not chosen as
  primary because the existing hardware pipeline is proven and the enhancement goal needs
  a GPU path anyway.
- **MonoGame / FNA.** Frameworks with their own content pipeline and abstractions in the
  way; FNA is SDL-based but still a layer we would fight for parity details.
- **Silk.NET.Windowing (GLFW) + OpenAL.** Viable, but two native libraries instead of one,
  no audio in GLFW, and OpenAL's mixer replaces ours. SDL gives window, input, gamepad and
  audio device in a single dependency.
- **OpenTK** instead of Silk.NET for GL. Equivalent for our needs; Silk chosen for its thin
  function-pointer loader (fits SDL-owned contexts) and active maintenance.
- **Veldrid.** Unmaintained. **Avalonia.** A UI toolkit, not a game host.

## Consequences

Positive:
- Linux and Windows from one code path and one CI matrix; no Windows-only TFM anywhere.
- The god class is split into testable pieces; `Host` logic (request servicing, frame
  ordering, mixer) gets unit tests it cannot have today.
- Palette-indexed shading becomes a first-class renderer design, closing a listed parity gap.
- Oracle work moves to native Linux DOSBox-X with no change of oracle (see `_research/README.md`).

Negative / risks:
- Vortice, NAudio and System.Drawing knowledge in the current code is discarded; the HLSL
  shader is rewritten in GLSL (small).
- `ppy.SDL3-CS` tracks SDL3 closely and its API surface may shift between releases; pin the
  version and upgrade deliberately.
- Silk.NET 3.0 is still unreleased; staying on 2.x is fine, but the eventual move is a
  (mechanical) breaking change.
- Relative-mouse mode and high-DPI behave differently on Wayland vs X11 vs Win32; input
  calibration (`MICKEY_X/Y`, `MOUSE_SENSE`) must be re-verified per platform against the
  oracle, not assumed from the Windows build.
- macOS is not a target; OpenGL is deprecated there. If it ever becomes one, SDL_GPU or a
  Metal-capable path is the answer, recorded in a new ADR.

Follow-ups this ADR creates:
- Mark ADR-0001 superseded; update `README.md` goals ("Native & modern … Direct3D") and
  `docs/recon/05`/`06` notes that assume Vortice/DirectX.
- ADR-0010 (proposed next; 0009 was taken by the render-space handedness decision): oracle
  hosts on Linux — DOSBox-X native as primary, `WVRUN.EXE` under Wine as a secondary oracle
  for DOS-vs-Windows runtime differences only.
- Add `oracle prepare` `.sh` launcher output and a Linux DOSBox-X config to `ovtool`.
