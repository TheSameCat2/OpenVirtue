// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Host;
using Silk.NET.OpenGL;

namespace OpenVirtue.App.Rendering;

/// <summary>
/// OpenGL 3.3 core renderer. Textures are point-sampled RGBA. Palette-index shading
/// (ADR-0008 decision 3) is the next renderer change, after projection frames match
/// the DOSBox-X captures; this pass keeps the same tint and ambient multiply the
/// Direct3D host used.
/// </summary>
public sealed unsafe class GlRenderBackend : IRenderBackend, IDisposable
{
    private const string VertexShader = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in float aAmbient;
        uniform mat4 uViewProjection;
        out vec2 vUv;
        out float vAmbient;
        void main() {
            // uViewProjection is a System.Numerics row-vector matrix. It is uploaded with
            // glUniformMatrix4fv transpose=false, so GL reads that memory as column-major.
            // That is the same implicit transpose the D3D11 constant buffer applied.
            gl_Position = uViewProjection * vec4(aPos, 1.0);
            vUv = aUv;
            vAmbient = aAmbient;
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        in vec2 vUv;
        in float vAmbient;
        uniform sampler2D uTexture;
        uniform vec4 uTintAndLight;
        uniform float uAlphaTest;
        out vec4 fragColor;
        void main() {
            vec4 color = texture(uTexture, vUv);
            if (uAlphaTest > 0.5 && color.a < 0.5) {
                discard;
            }
            float light = clamp(uTintAndLight.a + vAmbient, 0.0, 1.0);
            fragColor = vec4(clamp(color.rgb * uTintAndLight.rgb * light, 0.0, 1.0), 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly Action _present;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _program;
    private readonly int _viewProjectionLocation;
    private readonly int _tintLocation;
    private readonly int _alphaTestLocation;
    private readonly Dictionary<string, uint> _world = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _ui = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _white;
    private uint _dynamic;
    private bool _disposed;

    public GlRenderBackend(Func<string, nint> getProcAddress, Action present, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(getProcAddress);
        ArgumentNullException.ThrowIfNull(present);
        _present = present;
        _gl = GL.GetApi(getProcAddress);
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthRange(0.0, 1.0);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        uint stride = (uint)sizeof(RenderVertex);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 12);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 20);

        _program = LinkProgram(VertexShader, FragmentShader);
        _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
        _tintLocation = _gl.GetUniformLocation(_program, "uTintAndLight");
        _alphaTestLocation = _gl.GetUniformLocation(_program, "uAlphaTest");
        _gl.UseProgram(_program);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _white = CreateTexture(new TextureImage(1, 1, [255, 255, 255, 255]));
        Version = _gl.GetStringS(StringName.Version) ?? "";
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string Version { get; }

    public void Resize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    public void UploadWorld(IReadOnlyDictionary<string, TextureImage> images) => Replace(_world, images);

    public void UploadUi(IReadOnlyDictionary<string, TextureImage> images) => Replace(_ui, images);

    public void UploadFonts(IReadOnlyDictionary<string, TextureImage> images) => Replace(_fonts, images);

    public void Draw(HostFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Width = Math.Max(1, frame.Width);
        Height = Math.Max(1, frame.Height);
        _gl.BindVertexArray(_vao);
        _gl.UseProgram(_program);
        _gl.DepthMask(true);
        _gl.ClearColor(frame.ClearColor.X, frame.ClearColor.Y, frame.ClearColor.Z, frame.ClearColor.W);
        uint mask = (uint)ClearBufferMask.ColorBufferBit;
        if (frame.ClearDepth)
        {
            _gl.ClearDepth(1.0);
            mask |= (uint)ClearBufferMask.DepthBufferBit;
        }

        _gl.Clear(mask);
        foreach (DrawBatch batch in frame.Batches)
        {
            if (batch.Vertices.Length == 0)
            {
                continue;
            }

            int glY = Height - (batch.Viewport.Y + batch.Viewport.Height);
            _gl.Viewport(batch.Viewport.X, glY, (uint)batch.Viewport.Width, (uint)batch.Viewport.Height);
            if (batch.DepthTest)
            {
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthMask(true);
            }
            else
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
            }

            BindBatchTexture(batch);
            UploadMatrix(batch.Transform);
            _gl.Uniform4(_tintLocation, batch.Tint.X, batch.Tint.Y, batch.Tint.Z, batch.Ambient);
            _gl.Uniform1(_alphaTestLocation, batch.AlphaTest ? 1f : 0f);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, batch.Vertices.AsSpan(), BufferUsageARB.DynamicDraw);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)batch.Vertices.Length);
        }
    }

    public RgbaImage ReadRgba()
    {
        int width = Math.Max(1, Width);
        int height = Math.Max(1, Height);
        byte[] raw = new byte[width * height * 4];
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, raw);
        byte[] flipped = new byte[raw.Length];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            raw.AsSpan((height - 1 - y) * stride, stride).CopyTo(flipped.AsSpan(y * stride, stride));
        }

        return new RgbaImage(width, height, flipped);
    }

    public void Present() => _present();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeleteAll(_world);
        DeleteAll(_ui);
        DeleteAll(_fonts);
        if (_dynamic != 0)
        {
            _gl.DeleteTexture(_dynamic);
        }

        _gl.DeleteTexture(_white);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.Dispose();
    }

    private void BindBatchTexture(DrawBatch batch)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (batch.DynamicImage is { } dynamic)
        {
            if (_dynamic == 0)
            {
                _dynamic = _gl.GenTexture();
            }

            UploadImage(_dynamic, dynamic);
            _gl.BindTexture(TextureTarget.Texture2D, _dynamic);
            return;
        }

        Dictionary<string, uint> cache = batch.Slot switch
        {
            TextureSlot.Ui => _ui,
            TextureSlot.Font => _fonts,
            _ => _world,
        };
        uint texture = batch.Texture is not null && cache.TryGetValue(batch.Texture, out uint found) ? found : _white;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    private void UploadMatrix(Matrix4x4 matrix)
    {
        // Row-major System.Numerics order. transpose=false makes GL treat it as column-major.
        Span<float> values = stackalloc float[16]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        };
        _gl.UniformMatrix4(_viewProjectionLocation, 1, false, values);
    }

    private void Replace(Dictionary<string, uint> cache, IReadOnlyDictionary<string, TextureImage> images)
    {
        DeleteAll(cache);
        foreach ((string name, TextureImage image) in images)
        {
            if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length < image.Width * image.Height * 4)
            {
                continue;
            }

            cache[name] = CreateTexture(image);
        }
    }

    private uint CreateTexture(TextureImage image)
    {
        uint texture = _gl.GenTexture();
        UploadImage(texture, image);
        return texture;
    }

    private void UploadImage(uint texture, TextureImage image)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            (uint)image.Width,
            (uint)image.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            image.Rgba);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    private void DeleteAll(Dictionary<string, uint> cache)
    {
        foreach (uint texture in cache.Values)
        {
            _gl.DeleteTexture(texture);
        }

        cache.Clear();
    }

    private uint LinkProgram(string vertexSource, string fragmentSource)
    {
        uint vertex = Compile(ShaderType.VertexShader, vertexSource);
        uint fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            throw new InvalidOperationException("GL program link failed: " + _gl.GetProgramInfoLog(program));
        }

        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            throw new InvalidOperationException($"GL {type} compile failed: {_gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }
}
