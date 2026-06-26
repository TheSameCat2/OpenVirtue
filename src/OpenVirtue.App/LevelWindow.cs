// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using System.Runtime.InteropServices;
using OpenVirtue.Engine;
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
/// textured from the level's PCX bitmaps, with a depth buffer and a fly camera.
/// </summary>
internal sealed class LevelWindow : Form
{
    private const string ShaderSource =
        """
        cbuffer Frame : register(b0) { float4x4 ViewProjection; };
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
            return float4(color.rgb, 1.0);
        }
        """;

    private readonly Level _level;
    private readonly IReadOnlyDictionary<string, TextureImage> _textureImages;
    private readonly Camera _camera = new();
    private readonly HashSet<Keys> _keysDown = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly List<(int Start, int Count, string? Texture)> _batches = [];
    private readonly Dictionary<string, ID3D11ShaderResourceView> _textureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Billboard>> _spriteGroups = new(StringComparer.OrdinalIgnoreCase);

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

    public LevelWindow(Level level, IReadOnlyDictionary<string, TextureImage> textures)
    {
        _level = level;
        _textureImages = textures;
        Text = $"OpenVirtue — {level.Name}";
        ClientSize = new System.Drawing.Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        PlaceCameraAtStart();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
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
        _timer.Tick += (_, _) => RenderFrame();
        _timer.Start();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (_swapChain is not null && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            ResizeBuffers();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) => _keysDown.Add(e.KeyCode);

    protected override void OnKeyUp(KeyEventArgs e) => _keysDown.Remove(e.KeyCode);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _lastMouse = e.Location;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e) => _dragging = false;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            _camera.Rotate((e.X - _lastMouse.X) * 0.005f, -(e.Y - _lastMouse.Y) * 0.005f);
            _lastMouse = e.Location;
        }
    }

    private void PlaceCameraAtStart()
    {
        if (_level.PlayerStart is { } start)
        {
            float floor = 0f;
            float ceiling = 100f;
            if (start.Region >= 0 && start.Region < _level.Regions.Count)
            {
                var region = _level.Regions[start.Region];
                floor = (float)region.FloorHeight;
                ceiling = (float)region.CeilHeight;
            }

            // Spawn at eye level: midway between floor and ceiling of the start region.
            float eye = ceiling > floor ? (floor + ceiling) * 0.5f : floor + 20f;
            _camera.Position = new Vector3(start.X, eye, start.Y);
            _camera.Yaw = -start.Angle * (MathF.PI / 180f);
        }
        else
        {
            _camera.Position = new Vector3(0, 50, 0);
        }
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
            Width = (uint)ClientSize.Width,
            Height = (uint)ClientSize.Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, Handle, description);

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
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);

        _depthTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)ClientSize.Width,
            Height = (uint)ClientSize.Height,
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
        _renderTarget.Dispose();
        _depthView.Dispose();
        _depthTexture.Dispose();
        _swapChain.ResizeBuffers(2, (uint)ClientSize.Width, (uint)ClientSize.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
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
            _batches.Add((vertices.Count, batch.Vertices.Count, batch.Texture));
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

    private void BuildSprites()
    {
        foreach (var thing in _level.Things)
        {
            AddSprite(thing.X, thing.Y, thing.Height, thing.Region, thing.Texture);
        }

        foreach (var actor in _level.Actors)
        {
            AddSprite(actor.X, actor.Y, actor.Height, actor.Region, actor.Texture);
        }
    }

    private void AddSprite(double x, double y, double height, int region, string? texture)
    {
        if (texture is null || !_level.Textures.TryGetValue(texture, out LevelTexture levelTexture))
        {
            return;
        }

        // World height comes from the WDL HEIGHT (designer-intended, room-scaled);
        // width follows the texture's pixel aspect ratio.
        float worldHeight = height > 0 ? (float)height : 1f;
        float aspect = levelTexture.Height > 0 ? levelTexture.Width / (float)levelTexture.Height : 1f;
        float worldWidth = worldHeight * aspect;
        float floor = region >= 0 && region < _level.Regions.Count ? (float)_level.Regions[region].FloorHeight : 0f;

        if (!_spriteGroups.TryGetValue(texture, out List<Billboard>? group))
        {
            group = [];
            _spriteGroups[texture] = group;
        }

        group.Add(new Billboard(new Vector3((float)x, floor, (float)y), worldWidth * 0.5f, worldHeight * 0.5f));
    }

    private void RenderSprites()
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
        // Per-tick move distance (~60 ticks/sec): Ctrl = precise, Shift = fast, default = moderate.
        float speed = 10f;
        if (_keysDown.Contains(Keys.ShiftKey))
        {
            speed = 40f;
        }
        else if (_keysDown.Contains(Keys.ControlKey))
        {
            speed = 3f;
        }

        if (_keysDown.Contains(Keys.W)) _camera.MoveForward(speed);
        if (_keysDown.Contains(Keys.S)) _camera.MoveForward(-speed);
        if (_keysDown.Contains(Keys.A)) _camera.MoveRight(-speed);
        if (_keysDown.Contains(Keys.D)) _camera.MoveRight(speed);
        if (_keysDown.Contains(Keys.E)) _camera.MoveUp(speed);
        if (_keysDown.Contains(Keys.Q)) _camera.MoveUp(-speed);
    }

    private void RenderFrame()
    {
        if (_device is null || _vertexCount == 0)
        {
            return;
        }

        UpdateCamera();

        float aspect = (float)ClientSize.Width / Math.Max(1, ClientSize.Height);
        var constants = new FrameConstants { ViewProjection = _camera.View * _camera.Projection(aspect) };
        _context.UpdateSubresource(constants, _constantBuffer);

        _context.RSSetViewport(new Viewport(0, 0, ClientSize.Width, ClientSize.Height, 0, 1));
        _context.RSSetState(_rasterizer);
        _context.OMSetRenderTargets(_renderTarget, _depthView);
        _context.OMSetDepthStencilState(_depthState);
        _context.ClearRenderTargetView(_renderTarget, new Color4(0.08f, 0.10f, 0.14f, 1f));
        _context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth, 1f, 0);

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<RenderVertex>());
        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _sampler);

        foreach ((int start, int count, string? texture) in _batches)
        {
            ID3D11ShaderResourceView view = texture is not null && _textureViews.TryGetValue(texture, out ID3D11ShaderResourceView? found)
                ? found
                : _defaultView;
            _context.PSSetShaderResource(0, view);
            _context.Draw((uint)count, (uint)start);
        }

        RenderSprites();

        _swapChain.Present(1, PresentFlags.None);
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
