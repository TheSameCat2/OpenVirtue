// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenVirtue.Engine;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Rendering;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace OpenVirtue.App;

/// <summary>
/// A WinForms window hosting a Direct3D 11 view of a loaded level: the wall geometry,
/// textured from the level's PCX bitmaps, with a depth buffer and debug walking status.
/// </summary>
internal sealed class LevelWindow : Form
{
    private static readonly Color4 DefaultClearColor = new(0.08f, 0.10f, 0.14f, 1f);

    private const string ShaderSource =
        """
        cbuffer Frame : register(b0) { float4x4 ViewProjection; float4 Tint; };
        Texture2D Diffuse : register(t0);
        SamplerState Sampler : register(s0);

        struct VSInput  { float3 pos : POSITION; float2 uv : TEXCOORD0; };
        struct VSOutput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VSOutput VSMain(VSInput input)
        {
            VSOutput o;
            o.pos = mul(ViewProjection, float4(input.pos, 1.0));
            o.uv = input.uv;
            return o;
        }

        float4 PSMain(VSOutput input) : SV_Target
        {
            float4 color = Diffuse.Sample(Sampler, input.uv);
            clip(color.a - 0.5);          // alpha-test cutout for color-keyed sprites
            return float4(saturate(color.rgb * Tint.rgb), 1.0);
        }
        """;

    private readonly Level _level;
    private readonly IReadOnlyDictionary<string, TextureImage> _textureImages;
    private readonly Color4 _clearColor;
    private readonly Panel _renderSurface = new()
    {
        BackColor = System.Drawing.Color.Black,
        Dock = DockStyle.Fill,
        TabStop = true,
    };
    private readonly StatusStrip _statusStrip = new() { SizingGrip = false };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Camera _camera = new();
    private Player _player = null!;
    private readonly HashSet<Keys> _keysDown = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly List<(int Start, int Count, string? Texture, float Ambient)> _batches = [];
    private readonly Dictionary<string, ID3D11ShaderResourceView> _textureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Billboard>> _spriteGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly WdlRuntime _runtime;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _renderTarget = null!;
    private ID3D11Texture2D _depthTexture = null!;
    private ID3D11DepthStencilView _depthView = null!;
    private ID3D11VertexShader _vertexShader = null!;
    private ID3D11PixelShader _pixelShader = null!;
    private ID3D11InputLayout _inputLayout = null!;
    private ID3D11Buffer _vertexBuffer = null!;
    private ID3D11Buffer _constantBuffer = null!;
    private ID3D11RasterizerState _rasterizer = null!;
    private ID3D11DepthStencilState _depthState = null!;
    private ID3D11SamplerState _sampler = null!;
    private ID3D11ShaderResourceView _defaultView = null!;
    private int _vertexCount;

    private Point _lastMouse;
    private bool _dragging;
    private string _baseTitle = "";
    private bool _startupRan;
    private double _lastFrameSeconds;
    private double _lastTimeCorrection;
    private double _statusAccum;

    public LevelWindow(Level level, IReadOnlyDictionary<string, TextureImage> textures)
    {
        _level = level;
        _textureImages = textures;
        _clearColor = ChooseBackgroundColor();
        _runtime = new WdlRuntime(level);
        Text = $"OpenVirtue — {level.Name}";
        ClientSize = new System.Drawing.Size(1280, 720);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        _renderSurface.MouseDown += RenderSurface_MouseDown;
        _renderSurface.MouseUp += RenderSurface_MouseUp;
        _renderSurface.MouseMove += RenderSurface_MouseMove;
        _renderSurface.Resize += (_, _) => ResizeBuffers();
        _statusStrip.Items.Add(_statusLabel);
        Controls.Add(_renderSurface);
        Controls.Add(_statusStrip);
        PlaceCameraAtStart();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Tint;
    }

    /// <summary>A camera-facing sprite: its base point (on the floor) and half-extents in world units.</summary>
    private readonly record struct Billboard(Vector3 BasePosition, float HalfWidth, float HalfHeight);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeDirect3D();
        LoadTextures();
        BuildGeometry();
        BuildSprites();
        BootRuntime();
        _timer.Tick += (_, _) => RenderFrame();
        _timer.Start();
    }

    /// <summary>Boots the level's WDL runtime: runs its IF_START script and shows status in the strip.</summary>
    private void BootRuntime()
    {
        try
        {
            _startupRan = _runtime.RunStartup(); // run the level's IF_START script, if any
        }
        catch
        {
            // A startup-script fault must not take down the viewer.
        }

        _baseTitle = $"OpenVirtue — {_level.Name}";
        Text = _baseTitle;
        UpdateStatus();
        _frameClock.Restart(); // discard level-load time before the first real frame delta
    }

    /// <summary>Advances the runtime one frame and surfaces the live tick in the status strip.</summary>
    private void TickRuntime()
    {
        _lastFrameSeconds = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        _lastTimeCorrection = _runtime.Tick(_lastFrameSeconds);

        _statusAccum += _lastFrameSeconds;
        if (_statusAccum >= 0.25)
        {
            _statusAccum = 0;
            UpdateStatus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) => _keysDown.Add(e.KeyCode);

    protected override void OnKeyUp(KeyEventArgs e) => _keysDown.Remove(e.KeyCode);

    private void RenderSurface_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _renderSurface.Focus();
            _dragging = true;
            _lastMouse = e.Location;
        }
    }

    private void RenderSurface_MouseUp(object? sender, MouseEventArgs e) => _dragging = false;

    private void RenderSurface_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            _camera.Rotate((e.X - _lastMouse.X) * 0.005f, -(e.Y - _lastMouse.Y) * 0.005f);
            _lastMouse = e.Location;
        }
    }

    private void PlaceCameraAtStart()
    {
        // The player owns position (floor-following physics); the camera is its eye.
        _player = new Player(_level);
        SpawnInLargestRoom(); // start in a big open room — easier to get oriented and test movement
        _runtime.RegisterObject("player", _player);
        _camera.Position = _player.Position;
        if (_level.PlayerStart is { } start)
        {
            _camera.Yaw = -start.Angle * (MathF.PI / 180f);
        }
    }

    private void SpawnInLargestRoom()
    {
        var bounds = new Dictionary<int, (float MinX, float MinZ, float MaxX, float MaxZ)>();
        void Accumulate(int region, float vx, float vz)
        {
            if (region < 0 || region >= _level.Regions.Count)
            {
                return;
            }

            bounds[region] = bounds.TryGetValue(region, out var b)
                ? (MathF.Min(b.MinX, vx), MathF.Min(b.MinZ, vz), MathF.Max(b.MaxX, vx), MathF.Max(b.MaxZ, vz))
                : (vx, vz, vx, vz);
        }

        foreach (var wall in _level.Walls)
        {
            foreach (int vi in new[] { wall.Vertex1, wall.Vertex2 })
            {
                if (vi >= 0 && vi < _level.Vertices.Count)
                {
                    var v = _level.Vertices[vi];
                    Accumulate(wall.LeftRegion, v.X, v.Y);
                    Accumulate(wall.RightRegion, v.X, v.Y);
                }
            }
        }

        int best = -1;
        float bestArea = 0, cx = 0, cz = 0;
        foreach ((int region, var b) in bounds)
        {
            var r = _level.Regions[region];
            if (r.CeilHeight - r.FloorHeight < 5)
            {
                continue; // skip solid/thin regions
            }

            float area = (b.MaxX - b.MinX) * (b.MaxZ - b.MinZ);
            if (area > bestArea)
            {
                bestArea = area;
                best = region;
                cx = (b.MinX + b.MaxX) * 0.5f;
                cz = (b.MinZ + b.MaxZ) * 0.5f;
            }
        }

        if (best >= 0)
        {
            _player.MoveTo(best, cx, cz);
        }

        UpdateStatus();
    }

    private void InitializeDirect3D()
    {
        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device,
            out _context).CheckError();

        using IDXGIFactory2 factory = CreateDXGIFactory1<IDXGIFactory2>();
        var description = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _renderSurface.ClientSize.Width),
            Height = (uint)Math.Max(1, _renderSurface.ClientSize.Height),
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, _renderSurface.Handle, description);

        CreateSizedResources();

        _rasterizer = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            DepthClipEnable = true,
        });
        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);
        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            MaxLOD = float.MaxValue,
        });

        ReadOnlyMemory<byte> vsBytecode = Compiler.Compile(ShaderSource, "VSMain", "level.hlsl", "vs_5_0");
        ReadOnlyMemory<byte> psBytecode = Compiler.Compile(ShaderSource, "PSMain", "level.hlsl", "ps_5_0");
        _vertexShader = _device.CreateVertexShader(vsBytecode.Span);
        _pixelShader = _device.CreatePixelShader(psBytecode.Span);
        _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
            ],
            vsBytecode.Span);

        _constantBuffer = _device.CreateBuffer(
            new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
    }

    private void CreateSizedResources()
    {
        System.Drawing.Size renderSize = RenderSize();
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);

        _depthTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)renderSize.Width,
            Height = (uint)renderSize.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
        });
        _depthView = _device.CreateDepthStencilView(_depthTexture);
    }

    private void ResizeBuffers()
    {
        if (_swapChain is null || _device is null)
        {
            return;
        }

        System.Drawing.Size renderSize = RenderSize();
        _renderTarget.Dispose();
        _depthView.Dispose();
        _depthTexture.Dispose();
        _swapChain.ResizeBuffers(2, (uint)renderSize.Width, (uint)renderSize.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        CreateSizedResources();
    }

    private void LoadTextures()
    {
        _defaultView = UploadTexture(new TextureImage(1, 1, [255, 255, 255, 255]));
        foreach ((string name, TextureImage image) in _textureImages)
        {
            try
            {
                _textureViews[name] = UploadTexture(image);
            }
            catch
            {
                // Skip a texture that fails to upload rather than failing the whole view.
            }
        }
    }

    private ID3D11ShaderResourceView UploadTexture(TextureImage image)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)image.Width,
            Height = (uint)image.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };

        unsafe
        {
            fixed (byte* pixels = image.Rgba)
            {
                var data = new SubresourceData((nint)pixels, (uint)(image.Width * 4));
                using ID3D11Texture2D texture = _device.CreateTexture2D(description, [data]);
                return _device.CreateShaderResourceView(texture);
            }
        }
    }

    private void BuildGeometry()
    {
        LevelMesh mesh = MeshBuilder.Build(_level);
        var vertices = new List<RenderVertex>();
        foreach (MeshBatch batch in mesh.Batches)
        {
            _batches.Add((vertices.Count, batch.Vertices.Count, batch.Texture, TextureAmbient(batch.Texture)));
            vertices.AddRange(batch.Vertices);
        }

        _vertexCount = vertices.Count;
        if (_vertexCount == 0)
        {
            return;
        }

        _vertexBuffer = _device.CreateBuffer(
            vertices.ToArray().AsSpan(),
            new BufferDescription((uint)(_vertexCount * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));
    }

    // World units per sprite-bitmap pixel. WDL HEIGHT turned out to be a placement value
    // (almost always 1), not a visual size, so sprites are sized from their bitmap. Calibrated
    // so the ~174px-tall enemy reads ~7 units; an easy single knob to tune.
    private const float SpriteWorldPerPixel = 0.04f;

    private void BuildSprites()
    {
        foreach (var thing in _level.Things)
        {
            AddSprite(thing.X, thing.Y, thing.Region, thing.Texture);
        }

        foreach (var actor in _level.Actors)
        {
            AddSprite(actor.X, actor.Y, actor.Region, actor.Texture);
        }
    }

    private void AddSprite(double x, double y, int region, string? texture)
    {
        if (texture is null || !_level.Textures.TryGetValue(texture, out LevelTexture levelTexture) || levelTexture.Height <= 0)
        {
            return;
        }

        float worldWidth = levelTexture.Width * SpriteWorldPerPixel;
        float worldHeight = levelTexture.Height * SpriteWorldPerPixel;
        float floor = region >= 0 && region < _level.Regions.Count ? (float)_level.Regions[region].FloorHeight : 0f;

        if (!_spriteGroups.TryGetValue(texture, out List<Billboard>? group))
        {
            group = [];
            _spriteGroups[texture] = group;
        }

        group.Add(new Billboard(new Vector3((float)x, floor, (float)y), worldWidth * 0.5f, worldHeight * 0.5f));
    }

    private void RenderSprites(Matrix4x4 viewProjection)
    {
        if (_spriteGroups.Count == 0)
        {
            return;
        }

        // Cylindrical billboards: face the camera horizontally, stay upright.
        Vector3 right = _camera.Right;
        Vector3 up = Vector3.UnitY;

        foreach ((string texture, List<Billboard> billboards) in _spriteGroups)
        {
            ApplyFrameConstants(viewProjection, TextureAmbient(texture));
            var vertices = new List<RenderVertex>(billboards.Count * 6);
            foreach (Billboard billboard in billboards)
            {
                Vector3 r = right * billboard.HalfWidth;
                Vector3 u = up * billboard.HalfHeight;
                Vector3 center = billboard.BasePosition + new Vector3(0, billboard.HalfHeight, 0);
                Vector3 bottomLeft = center - r - u;
                Vector3 bottomRight = center + r - u;
                Vector3 topRight = center + r + u;
                Vector3 topLeft = center - r + u;

                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, 0, 1));
                vertices.Add(new RenderVertex(bottomRight.X, bottomRight.Y, bottomRight.Z, 1, 1));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, 1, 0));
                vertices.Add(new RenderVertex(bottomLeft.X, bottomLeft.Y, bottomLeft.Z, 0, 1));
                vertices.Add(new RenderVertex(topRight.X, topRight.Y, topRight.Z, 1, 0));
                vertices.Add(new RenderVertex(topLeft.X, topLeft.Y, topLeft.Z, 0, 0));
            }

            using ID3D11Buffer buffer = _device.CreateBuffer(
                vertices.ToArray().AsSpan(),
                new BufferDescription((uint)(vertices.Count * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));

            ID3D11ShaderResourceView view = _textureViews.TryGetValue(texture, out ID3D11ShaderResourceView? found)
                ? found
                : _defaultView;

            _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<RenderVertex>());
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)vertices.Count, 0);
        }
    }

    private void UpdateCamera()
    {
        // Walk on the floor: WASD move horizontally (collision + region tracking via the player),
        // Space jumps, the mouse looks. Shift runs, Ctrl creeps.
        float speed = _keysDown.Contains(Keys.ShiftKey) ? 22f : _keysDown.Contains(Keys.ControlKey) ? 3f : 8f;

        float yaw = _camera.Yaw;
        var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)); // horizontal facing
        Vector3 right = _camera.Right;                                // horizontal (cross with world up)

        var move = Vector3.Zero;
        if (_keysDown.Contains(Keys.W)) move += forward;
        if (_keysDown.Contains(Keys.S)) move -= forward;
        if (_keysDown.Contains(Keys.D)) move += right;
        if (_keysDown.Contains(Keys.A)) move -= right;

        if (move != Vector3.Zero)
        {
            move = Vector3.Normalize(move) * speed;
            _player.MoveHorizontal(move.X, move.Z);
        }

        if (_keysDown.Contains(Keys.Space))
        {
            _player.Jump();
        }

        _player.Tick();
        _camera.Position = _player.Position;
    }

    private void RenderFrame()
    {
        if (_device is null || _vertexCount == 0)
        {
            return;
        }

        UpdateCamera();
        TickRuntime();

        System.Drawing.Size renderSize = RenderSize();
        float aspect = (float)renderSize.Width / Math.Max(1, renderSize.Height);
        Matrix4x4 viewProjection = _camera.View * Camera.Projection(aspect);

        _context.RSSetViewport(new Viewport(0, 0, renderSize.Width, renderSize.Height, 0, 1));
        _context.RSSetState(_rasterizer);
        _context.OMSetRenderTargets(_renderTarget, _depthView);
        _context.OMSetDepthStencilState(_depthState);
        _context.ClearRenderTargetView(_renderTarget, _clearColor);
        _context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth, 1f, 0);

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<RenderVertex>());
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _sampler);

        foreach ((int start, int count, string? texture, float ambient) in _batches)
        {
            ApplyFrameConstants(viewProjection, ambient);
            ID3D11ShaderResourceView view = texture is not null && _textureViews.TryGetValue(texture, out ID3D11ShaderResourceView? found)
                ? found
                : _defaultView;
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)count, (uint)start);
        }

        RenderSprites(viewProjection);

        _swapChain.Present(1, PresentFlags.None);
    }

    private System.Drawing.Size RenderSize() =>
        new(Math.Max(1, _renderSurface.ClientSize.Width), Math.Max(1, _renderSurface.ClientSize.Height));

    private float TextureAmbient(string? texture)
    {
        if (texture is not null && _level.Textures.TryGetValue(texture, out LevelTexture levelTexture))
        {
            return (float)Math.Clamp(levelTexture.Ambient, 0, 2);
        }

        return 1f;
    }

    private void ApplyFrameConstants(Matrix4x4 viewProjection, float ambient)
    {
        var constants = new FrameConstants
        {
            ViewProjection = viewProjection,
            Tint = new Vector4(ambient, ambient, ambient, 1f),
        };
        _context.UpdateSubresource(constants, _constantBuffer);
    }

    private Color4 ChooseBackgroundColor()
    {
        foreach (LevelTexture texture in _level.Textures.Values)
        {
            if (texture.IsSky && _textureImages.TryGetValue(texture.Name, out TextureImage image))
            {
                return AverageOpaqueColor(image);
            }
        }

        return DefaultClearColor;
    }

    private static Color4 AverageOpaqueColor(TextureImage image)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long count = 0;
        ReadOnlySpan<byte> rgba = image.Rgba;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }

            r += rgba[i];
            g += rgba[i + 1];
            b += rgba[i + 2];
            count++;
        }

        if (count == 0)
        {
            return DefaultClearColor;
        }

        float scale = 1.0f / (255.0f * count);
        return new Color4(r * scale, g * scale, b * scale, 1f);
    }

    private void UpdateStatus()
    {
        if (_player is null || _statusLabel is null)
        {
            return;
        }

        Vector3 p = _player.Position;
        string regionName = RegionName(_player.Region);
        double fps = _lastFrameSeconds > 0 ? 1.0 / _lastFrameSeconds : 0;
        (double floor, double ceiling) = RegionHeights(_player.Region);
        _statusLabel.Text =
            $"world {_level.Name} | pos {p.X:F1}, {p.Y:F1}, {p.Z:F1} | region {_player.Region} {regionName} | " +
            $"floor {floor:F1} ceil {ceiling:F1} | FPS {fps:F0} | TIME_CORR {_lastTimeCorrection:F3} | " +
            $"IF_START {(_level.StartupAction is null ? "none" : _startupRan ? "ran" : "failed")} | " +
            $"debug walking";
    }

    private string RegionName(int region) =>
        region >= 0 && region < _level.Regions.Count ? _level.Regions[region].Name : "(none)";

    private (double Floor, double Ceiling) RegionHeights(int region)
    {
        if (region >= 0 && region < _level.Regions.Count)
        {
            var r = _level.Regions[region];
            return (r.FloorHeight, r.CeilHeight);
        }

        return (double.NaN, double.NaN);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (ID3D11ShaderResourceView view in _textureViews.Values)
            {
                view.Dispose();
            }

            _defaultView?.Dispose();
            _sampler?.Dispose();
            _vertexBuffer?.Dispose();
            _constantBuffer?.Dispose();
            _inputLayout?.Dispose();
            _vertexShader?.Dispose();
            _pixelShader?.Dispose();
            _rasterizer?.Dispose();
            _depthState?.Dispose();
            _renderTarget?.Dispose();
            _depthView?.Dispose();
            _depthTexture?.Dispose();
            _swapChain?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
        }

        base.Dispose(disposing);
    }
}
