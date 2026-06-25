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
/// A WinForms window hosting a Direct3D 11 view of a loaded level. The first cut draws the
/// wall geometry flat-shaded (banded by wall length) with a depth buffer and a fly camera —
/// enough to navigate the level's structure. Textures are a follow-up.
/// </summary>
internal sealed class LevelWindow : Form
{
    private const string ShaderSource =
        """
        cbuffer Frame : register(b0) { float4x4 ViewProjection; };
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
            // Banded shading along the wall so geometry is legible without textures.
            float band = 0.45 + 0.40 * frac(input.uv.x * 0.02);
            float vshade = 0.7 + 0.3 * saturate(input.uv.y * 0.05);
            float s = band * vshade;
            return float4(s, s * 0.95, s * 0.85, 1.0);
        }
        """;

    private readonly Level _level;
    private readonly Camera _camera = new();
    private readonly HashSet<Keys> _keysDown = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };

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
    private int _vertexCount;

    private Point _lastMouse;
    private bool _dragging;

    public LevelWindow(Level level)
    {
        _level = level;
        Text = $"OpenVirtue — {level.Name}";
        ClientSize = new System.Drawing.Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        PlaceCameraAtStart();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitializeDirect3D();
        BuildGeometry();
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
            float floor = start.Region >= 0 && start.Region < _level.Regions.Count
                ? (float)_level.Regions[start.Region].FloorHeight
                : 0f;
            _camera.Position = new Vector3(start.X, floor + 25f, start.Y);
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

    private void BuildGeometry()
    {
        LevelMesh mesh = MeshBuilder.BuildWalls(_level);
        RenderVertex[] vertices = mesh.Batches.SelectMany(b => b.Vertices).ToArray();
        _vertexCount = vertices.Length;
        if (_vertexCount == 0)
        {
            return;
        }

        _vertexBuffer = _device.CreateBuffer(
            vertices.AsSpan(),
            new BufferDescription((uint)(vertices.Length * Marshal.SizeOf<RenderVertex>()), BindFlags.VertexBuffer, ResourceUsage.Immutable));
    }

    private void UpdateCamera()
    {
        float speed = (_keysDown.Contains(Keys.ShiftKey) ? 120f : 40f);
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

        _context.Draw((uint)_vertexCount, 0);
        _swapChain.Present(1, PresentFlags.None);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
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
