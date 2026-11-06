// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;
using OpenVirtue.Host;
using Silk.NET.OpenGL;

namespace OpenVirtue.App.Rendering;

/// <summary>
/// OpenGL 3.3 core renderer. Textures are point-sampled. Shaded batches (world and sprites when the
/// level's palette declares <c>RANGE</c>s, ADR-0008 decision 3) draw by palette index: the texel's
/// shade level comes from its depth, the light's zones, and <c>AMBIENT</c> as measured in
/// OV-20261105-shade-global — a wall steps its zones across the screen from its own end vertices —
/// the shade table maps index and level to an index, and the palette gives its colour. Other batches
/// keep the RGB tint and ambient multiply.
/// </summary>
public sealed unsafe class GlRenderBackend : IRenderBackend, IDisposable
{
    private const string VertexShader = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in float aAmbient;
        layout(location = 3) in vec4 aWall;
        uniform mat4 uViewProjection;
        uniform float uColumns;
        out vec2 vUv;
        out float vAmbient;
        out float vDepth;
        flat out float vIsWall;
        flat out float vWallSlope;
        flat out float vWallStart;
        void main() {
            // uViewProjection is a System.Numerics row-vector matrix. It is uploaded with
            // glUniformMatrix4fv transpose=false, so GL reads that memory as column-major.
            // That is the same implicit transpose the D3D11 constant buffer applied.
            gl_Position = uViewProjection * vec4(aPos, 1.0);
            vUv = aUv;
            vAmbient = aAmbient;
            // The perspective w is the depth along the line of sight.
            vDepth = gl_Position.w;

            // OV-20261105-shade-global: a wall steps its shade zones across the screen from its two
            // end vertices at a rounded slope. The ends' columns come from clip x and w alone: the
            // tilt is a y-shear, which leaves x where it was, so y 0 serves for both ends. Their
            // x/w and 1/w stay collinear even when one end is behind the eye, which keeps the slope
            // right there; the first visible column is then the window's left edge.
            vec4 clipA = uViewProjection * vec4(aWall.x, 0.0, aWall.y, 1.0);
            vec4 clipB = uViewProjection * vec4(aWall.z, 0.0, aWall.w, 1.0);
            vIsWall = (aWall.x != 0.0 || aWall.y != 0.0 || aWall.z != 0.0 || aWall.w != 0.0) ? 1.0 : 0.0;
            if (vIsWall > 0.5) {
                // The slope of 1/w per column, written without dividing by w so an end on the eye
                // plane (w = 0) stays finite: with N the columns,
                // colA - colB = 0.5*N*(xA*wB - xB*wA)/(wA*wB) and 1/wA - 1/wB = (wB - wA)/(wA*wB),
                // so their ratio is (wB - wA) / (0.5*N*(xA*wB - xB*wA)). Only a pair on one ray,
                // where that denominator vanishes, is degenerate, and its slope is 0.
                float columns = 0.5 * uColumns * (clipA.x * clipB.w - clipB.x * clipA.w);
                vWallSlope = abs(columns) > 1e-9 ? (clipB.w - clipA.w) / columns : 0.0;
                if (clipA.w > 0.0 && clipB.w > 0.0) {
                    float colA = (clipA.x / clipA.w * 0.5 + 0.5) * uColumns;
                    float colB = (clipB.x / clipB.w * 0.5 + 0.5) * uColumns;
                    vWallStart = max(min(colA, colB), 0.0);
                } else {
                    vWallStart = 0.0;
                }
            } else {
                vWallSlope = 0.0;
                vWallStart = 0.0;
            }
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        in vec2 vUv;
        in float vAmbient;
        in float vDepth;
        flat in float vIsWall;
        flat in float vWallSlope;
        flat in float vWallStart;
        uniform sampler2D uTexture;
        uniform sampler2D uPalette;
        uniform sampler2D uShade;
        uniform vec4 uTintAndLight;
        uniform vec4 uFadeCurrent;
        uniform vec4 uFadeTarget;
        uniform int uBlend;
        uniform int uSprite;
        uniform int uHeightParity;
        uniform float uAlphaTest;
        uniform float uShaded;
        uniform float uSurfaceQ;
        uniform float uWallQ;
        uniform float uPhi;
        uniform float uColumns;
        uniform vec2 uViewport;
        out vec4 fragColor;
        void main() {
            // OV-20261104-blend: GHOST shows its texels where the screen pixel's x + y (from the top
            // left) is odd; gl_FragCoord counts rows from the bottom, so the sum's parity flips with the
            // height's. uBlend 3 draws palette entry 0 (black) for DIAPHANOUS without a BLUR palette.
            if (uBlend == 1 && ((int(gl_FragCoord.x) + int(gl_FragCoord.y) + uHeightParity) & 1) == 0) {
                discard;
            }
            if (uShaded > 0.5) {
                // OV-20261105-shade-global: level = 2*floor((30 + phi - 32*A - 2*m)/2), m the light's
                // zones at the texel: ceil(Qf/d) for a surface, floor(Qw/d) for a sprite, and ceil of
                // a wall's stepped value; then index -> shaded index -> colour.
                float index = floor(texture(uTexture, vUv).r * 255.0 + 0.5);
                if (uAlphaTest > 0.5 && index < 0.5) {
                    discard;
                }
                float depth = max(vDepth, 0.001);
                float zones;
                if (uSprite > 0) {
                    zones = floor(uWallQ / depth);
                } else if (vIsWall > 0.5) {
                    // The column in the 3-D window's own pixels, then the slope rounded the way the
                    // original advances it: r = ceil(1024*s)/1024, and 0 when it is under a unit.
                    float column = floor((gl_FragCoord.x - uViewport.x) * uColumns / uViewport.y);
                    float slope = uWallQ * vWallSlope;
                    float units = 1024.0 * slope;
                    float step = abs(units) < 1e-3 ? 0.0 : ceil(units) / 1024.0;
                    zones = ceil((uWallQ / depth) + (column - vWallStart) * (step - slope));
                } else {
                    zones = ceil(uSurfaceQ / depth);
                }
                float level = 30.0 + uPhi - 32.0 * (uTintAndLight.a + vAmbient) - 2.0 * zones;
                // Past the range's end (level 32 and up) the texel shows the INFINITY colour, entry 1
                // (OV-20261104-genius).
                level = 2.0 * floor(level / 2.0);
                float shaded = level >= 32.0
                    ? 1.0
                    : floor(texelFetch(uShade, ivec2(int(clamp(level, 0.0, 30.0)), int(index)), 0).r * 255.0 + 0.5);
                // The palette already shows the fade (OV-20261104-fade-pal).
                fragColor = vec4(uBlend == 3 ? vec3(0.0) : texelFetch(uPalette, ivec2(int(shaded), 0), 0).rgb, uBlend == 2 ? 0.5 : 1.0);
                return;
            }
            vec4 color = texture(uTexture, vUv);
            if (uAlphaTest > 0.5 && color.a < 0.5) {
                discard;
            }
            float light = clamp(uTintAndLight.a + vAmbient, 0.0, 1.0);
            vec3 lit = clamp(color.rgb * light, 0.0, 1.0);
            // OV-20261104-fade-pal: toward the current palette's colour when it is uniform, then toward the target.
            vec3 faded = mix(mix(lit, uFadeCurrent.rgb, uFadeCurrent.a), uFadeTarget.rgb, uFadeTarget.a);
            // The DAC shows 6 bits a channel, widened as an emulator shows them (OV-20261104-retail-frames:
            // the title's gray 50 reads 48).
            vec3 dac = floor(clamp(faded, 0.0, 1.0) * 63.75 + 0.0001);
            faded = (dac * 4.0 + floor(dac / 16.0)) / 255.0;
            fragColor = vec4(uBlend == 3 ? vec3(0.0) : faded, uBlend == 2 ? 0.5 : 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly Action _present;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _program;
    private readonly int _viewProjectionLocation;
    private readonly int _tintLocation;
    private readonly int _fadeCurrentLocation;
    private readonly int _blendLocation;
    private readonly int _spriteLocation;
    private readonly int _heightParityLocation;
    private readonly int _fadeTargetLocation;
    private readonly int _alphaTestLocation;
    private readonly int _shadedLocation;
    private readonly int _surfaceQLocation;
    private readonly int _wallQLocation;
    private readonly int _phiLocation;
    private readonly int _columnsLocation;
    private readonly int _viewportLocation;
    private readonly Dictionary<string, uint> _worldIndices = new(StringComparer.OrdinalIgnoreCase);
    private uint _palette;
    private uint _shade;
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
        // OV-20261105-shade-global: the wall's two ends, (x, z) each, in render space.
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 24);

        _program = LinkProgram(VertexShader, FragmentShader);
        _viewProjectionLocation = _gl.GetUniformLocation(_program, "uViewProjection");
        _tintLocation = _gl.GetUniformLocation(_program, "uTintAndLight");
        _fadeCurrentLocation = _gl.GetUniformLocation(_program, "uFadeCurrent");
        _blendLocation = _gl.GetUniformLocation(_program, "uBlend");
        _spriteLocation = _gl.GetUniformLocation(_program, "uSprite");
        _heightParityLocation = _gl.GetUniformLocation(_program, "uHeightParity");
        _fadeTargetLocation = _gl.GetUniformLocation(_program, "uFadeTarget");
        _alphaTestLocation = _gl.GetUniformLocation(_program, "uAlphaTest");
        _shadedLocation = _gl.GetUniformLocation(_program, "uShaded");
        _surfaceQLocation = _gl.GetUniformLocation(_program, "uSurfaceQ");
        _wallQLocation = _gl.GetUniformLocation(_program, "uWallQ");
        _phiLocation = _gl.GetUniformLocation(_program, "uPhi");
        _columnsLocation = _gl.GetUniformLocation(_program, "uColumns");
        _viewportLocation = _gl.GetUniformLocation(_program, "uViewport");
        _gl.UseProgram(_program);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uPalette"), 1);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uShade"), 2);
        _white = CreateTexture(new TextureImage(1, 1, [255, 255, 255, 255]));
        Version = _gl.GetStringS(StringName.Version) ?? "";
        Renderer = _gl.GetStringS(StringName.Renderer) ?? "";
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string Version { get; }

    /// <summary>The GL_RENDERER string, which tells a hardware driver from a software one such as llvmpipe.</summary>
    public string Renderer { get; }

    public void Resize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    public void UploadWorld(IReadOnlyDictionary<string, TextureImage> images)
    {
        Replace(_world, images);
        DeleteAll(_worldIndices);
        foreach ((string name, TextureImage image) in images)
        {
            if (image.Indices is { } indices && image.Width > 0 && image.Height > 0 && indices.Length >= image.Width * image.Height)
            {
                uint texture = _gl.GenTexture();
                UploadBytes(texture, image.Width, image.Height, indices, InternalFormat.R8, PixelFormat.Red);
                _worldIndices[name] = texture;
            }
        }
    }

    public void UploadShading(byte[]? palette, byte[]? shadeTable)
    {
        if (palette is null || shadeTable is null || palette.Length < 256 * 3 || shadeTable.Length < 256 * 32)
        {
            return;
        }

        if (_palette == 0)
        {
            _palette = _gl.GenTexture();
            _shade = _gl.GenTexture();
        }

        UploadBytes(_palette, 256, 1, palette, InternalFormat.Rgb8, PixelFormat.Rgb);
        // Row y is a colour index, column x a level.
        UploadBytes(_shade, 32, 256, shadeTable, InternalFormat.R8, PixelFormat.Red);
    }

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
                _gl.DepthMask(batch.DepthWrite);
            }
            else
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
            }

            bool shaded = batch.Shaded && _palette != 0 && batch.Texture is not null &&
                _worldIndices.ContainsKey(batch.Texture);
            if (shaded)
            {
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _worldIndices[batch.Texture!]);
                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, _palette);
                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, _shade);
                _gl.ActiveTexture(TextureUnit.Texture0);
            }
            else
            {
                BindBatchTexture(batch);
            }

            _gl.Uniform1(_shadedLocation, shaded ? 1f : 0f);
            _gl.Uniform1(_surfaceQLocation, frame.Zones.X);
            _gl.Uniform1(_wallQLocation, frame.Zones.Y);
            _gl.Uniform1(_phiLocation, frame.Zones.Z);
            _gl.Uniform1(_columnsLocation, frame.Columns);
            _gl.Uniform2(_viewportLocation, (float)batch.Viewport.X, (float)batch.Viewport.Width);
            UploadMatrix(batch.Transform);
            _gl.Uniform4(_tintLocation, 1f, 1f, 1f, batch.Ambient);
            _gl.Uniform4(_fadeCurrentLocation, batch.Fade.Current.X, batch.Fade.Current.Y, batch.Fade.Current.Z, batch.Fade.CurrentWeight);
            _gl.Uniform4(_fadeTargetLocation, batch.Fade.Target.X, batch.Fade.Target.Y, batch.Fade.Target.Z, batch.Fade.Factor);
            _gl.Uniform1(_blendLocation, (int)batch.Blend);
            _gl.Uniform1(_spriteLocation, batch.SpriteMarker);
            _gl.Uniform1(_heightParityLocation, (Height - 1) & 1);
            if (batch.Blend == BatchBlend.Diaphanous)
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }
            else
            {
                _gl.Disable(EnableCap.Blend);
            }
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
        DeleteAll(_worldIndices);
        if (_palette != 0)
        {
            _gl.DeleteTexture(_palette);
            _gl.DeleteTexture(_shade);
        }

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

    private void UploadBytes(uint texture, int width, int height, byte[] data, InternalFormat internalFormat, PixelFormat format)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            (uint)width,
            (uint)height,
            0,
            format,
            PixelType.UnsignedByte,
            data);
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
