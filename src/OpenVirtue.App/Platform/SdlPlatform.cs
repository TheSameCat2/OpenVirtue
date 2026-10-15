// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using System.Runtime.InteropServices;
using OpenVirtue.Host;
using SDL;

namespace OpenVirtue.App.Platform;

/// <summary>
/// SDL3 window, GL 3.3 context, and absolute mouse/keyboard state. The cursor is hidden while
/// the pointer is over the window; the pointer is not grabbed, so <c>MOUSE_X</c>/<c>MOUSE_Y</c>
/// keep moving with the cursor.
/// </summary>
public sealed unsafe class SdlPlatform : IPlatformWindow, IInputSource, IDisposable
{
    private const uint EventQuit = 0x100;
    private const uint EventWindowResized = 0x206;
    private const uint EventWindowPixelSizeChanged = 0x207;
    private const uint EventWindowMouseEnter = 0x20C;
    private const uint EventWindowMouseLeave = 0x20D;
    private const uint EventWindowFocusGained = 0x20E;
    private const uint EventWindowFocusLost = 0x20F;
    private const uint EventWindowCloseRequested = 0x210;
    private const uint EventKeyDown = 0x300;
    private const uint EventKeyUp = 0x301;
    private const uint EventTextInput = 0x303;
    private const uint EventMouseMotion = 0x400;
    private const uint EventMouseButtonDown = 0x401;
    private const uint EventMouseButtonUp = 0x402;

    private readonly HashSet<HostKey> _down = [];
    private readonly List<HostKey> _pressed = [];
    private readonly HashSet<MouseButton> _mouseDown = [];
    private readonly List<MouseButton> _mousePressed = [];
    private readonly List<MouseButton> _mouseReleased = [];

    private SDL_Window* _window;
    private SDL_GLContextState* _context;
    private string _title;
    private bool _open = true;
    private bool _cursorVisible = true;
    private bool _prompting;
    private bool _promptDone;
    private bool _promptAccepted;
    private string _promptBuffer = "";
    private int _promptCapacity;
    private Vector2 _pendingMotion;
    private bool _disposed;

    private SdlPlatform(SDL_Window* window, SDL_GLContextState* context, string title, int width, int height)
    {
        _window = window;
        _context = context;
        _title = title;
        Width = width;
        Height = height;
        Focused = true;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            if (_window != null)
            {
                SDL3.SDL_SetWindowTitle(_window, _title);
            }
        }
    }

    public bool IsOpen => _open;

    public bool Focused { get; private set; }

    public bool PointerInside { get; private set; }

    public Vector2 PointerPosition { get; private set; }

    public IReadOnlyCollection<HostKey> Pressed => _pressed;

    /// <summary>Creates a GL 3.3 core window. On failure SDL is shut down so a later attempt can init again.</summary>
    public static bool TryCreate(
        int width,
        int height,
        string title,
        bool hidden,
        out SdlPlatform? platform,
        out string? error)
    {
        platform = null;
        error = null;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_AUDIO | SDL_InitFlags.SDL_INIT_EVENTS))
        {
            error = SDL3.SDL_GetError();
            return false;
        }

        SDL_Window* window = null;
        SDL_GLContextState* context = null;
        try
        {
            SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);
            SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DOUBLEBUFFER, 1);
            SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DEPTH_SIZE, 24);

            SDL_WindowFlags flags = SDL_WindowFlags.SDL_WINDOW_OPENGL;
            if (hidden)
            {
                flags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;
            }
            else
            {
                flags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
            }

            window = SDL3.SDL_CreateWindow(title, width, height, flags);
            if (window == null)
            {
                error = SDL3.SDL_GetError();
                return false;
            }

            context = SDL3.SDL_GL_CreateContext(window);
            if (context == null || !SDL3.SDL_GL_MakeCurrent(window, context))
            {
                error = SDL3.SDL_GetError();
                return false;
            }

            int pixelWidth = width;
            int pixelHeight = height;
            if (SDL3.SDL_GetWindowSizeInPixels(window, &pixelWidth, &pixelHeight))
            {
                pixelWidth = Math.Max(1, pixelWidth);
                pixelHeight = Math.Max(1, pixelHeight);
            }

            platform = new SdlPlatform(window, context, title, pixelWidth, pixelHeight);
            window = null;
            context = null;
            return true;
        }
        finally
        {
            if (context != null)
            {
                SDL3.SDL_GL_DestroyContext(context);
            }

            if (window != null)
            {
                SDL3.SDL_DestroyWindow(window);
            }

            if (platform is null)
            {
                SDL3.SDL_Quit();
            }
        }
    }

    public static nint GetProcAddress(string name) => SDL3.SDL_GL_GetProcAddress(name);

    public void Swap()
    {
        if (_window != null)
        {
            SDL3.SDL_GL_SwapWindow(_window);
        }
    }

    public void PumpEvents()
    {
        _pressed.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        SDL_Event e;
        while (SDL3.SDL_PollEvent(&e))
        {
            Dispatch(e, recordInput: true);
        }

        RefreshPixelSize();
    }

    public bool IsDown(HostKey key) => _down.Contains(key);

    public bool IsDown(MouseButton button) => _mouseDown.Contains(button);

    public bool WasPressed(MouseButton button) => _mousePressed.Contains(button);

    public bool WasReleased(MouseButton button) => _mouseReleased.Contains(button);

    public Vector2 ConsumeMotion()
    {
        Vector2 motion = _pendingMotion;
        _pendingMotion = Vector2.Zero;
        return motion;
    }

    public void Close() => _open = false;

    public void SetCursorVisible(bool visible)
    {
        if (_cursorVisible == visible)
        {
            return;
        }

        _cursorVisible = visible;
        if (visible)
        {
            SDL3.SDL_ShowCursor();
        }
        else
        {
            SDL3.SDL_HideCursor();
        }
    }

    public string? Prompt(string label, string initialValue, int capacity)
    {
        _prompting = true;
        _promptCapacity = capacity;
        _promptBuffer = initialValue ?? "";
        if (capacity > 0 && _promptBuffer.Length > capacity)
        {
            _promptBuffer = _promptBuffer[..capacity];
        }

        _promptDone = false;
        _promptAccepted = false;
        string savedTitle = _title;
        SDL3.SDL_StartTextInput(_window);
        try
        {
            while (_open && !_promptDone)
            {
                Title = $"{savedTitle} | {label}: {_promptBuffer}";
                SDL_Event e;
                if (!SDL3.SDL_WaitEvent(&e))
                {
                    break;
                }

                Dispatch(e, recordInput: false);
            }
        }
        finally
        {
            SDL3.SDL_StopTextInput(_window);
            _pressed.Clear();
            _mousePressed.Clear();
            _mouseReleased.Clear();
            _prompting = false;
            Title = savedTitle;
        }

        return _promptAccepted ? _promptBuffer : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _open = false;
        if (_context != null)
        {
            SDL3.SDL_GL_DestroyContext(_context);
            _context = null;
        }

        if (_window != null)
        {
            SDL3.SDL_DestroyWindow(_window);
            _window = null;
        }

        SDL3.SDL_Quit();
    }

    private void Dispatch(SDL_Event e, bool recordInput)
    {
        uint type = (uint)e.type;
        switch (type)
        {
            case EventQuit:
            case EventWindowCloseRequested:
                _open = false;
                if (_prompting)
                {
                    _promptDone = true;
                    _promptAccepted = false;
                }

                break;
            case EventWindowResized:
            case EventWindowPixelSizeChanged:
                RefreshPixelSize();
                break;
            case EventWindowMouseEnter:
                PointerInside = true;
                break;
            case EventWindowMouseLeave:
                PointerInside = false;
                break;
            case EventWindowFocusGained:
                Focused = true;
                break;
            case EventWindowFocusLost:
                Focused = false;
                break;
            case EventKeyDown:
                OnKey(e.key.scancode, down: true, repeat: e.key.repeat, recordInput);
                break;
            case EventKeyUp:
                OnKey(e.key.scancode, down: false, repeat: false, recordInput);
                break;
            case EventTextInput:
                if (_prompting)
                {
                    AppendPromptText(e.text.text);
                }

                break;
            case EventMouseMotion:
                PointerPosition = new Vector2(e.motion.x, e.motion.y);
                if (recordInput && !_prompting)
                {
                    _pendingMotion += new Vector2(e.motion.xrel, e.motion.yrel);
                }

                break;
            case EventMouseButtonDown:
            case EventMouseButtonUp:
                OnMouseButton(e.button.button, type == EventMouseButtonDown, e.button.x, e.button.y, recordInput);
                break;
        }
    }

    private void OnKey(SDL_Scancode scancode, bool down, bool repeat, bool recordInput)
    {
        if (_prompting)
        {
            if (down && !repeat)
            {
                int code = (int)scancode;
                if (code is 40 or 88)
                {
                    _promptAccepted = true;
                    _promptDone = true;
                }
                else if (code == 41)
                {
                    _promptAccepted = false;
                    _promptDone = true;
                }
                else if (code == 42 && _promptBuffer.Length > 0)
                {
                    _promptBuffer = _promptBuffer[..^1];
                }
            }

            return;
        }

        if (!recordInput || !TryMapScancode(scancode, out HostKey key))
        {
            return;
        }

        if (down)
        {
            if (!repeat && _down.Add(key))
            {
                _pressed.Add(key);
            }
        }
        else
        {
            _down.Remove(key);
        }
    }

    private void OnMouseButton(byte button, bool down, float x, float y, bool recordInput)
    {
        PointerPosition = new Vector2(x, y);
        if (!recordInput || _prompting || !TryMapButton(button, out MouseButton mapped))
        {
            return;
        }

        if (down)
        {
            if (_mouseDown.Add(mapped))
            {
                _mousePressed.Add(mapped);
            }
        }
        else if (_mouseDown.Remove(mapped))
        {
            _mouseReleased.Add(mapped);
        }
    }

    private void AppendPromptText(byte* text)
    {
        if (text == null || _promptCapacity <= 0)
        {
            return;
        }

        string? incoming = Marshal.PtrToStringUTF8((nint)text);
        if (string.IsNullOrEmpty(incoming))
        {
            return;
        }

        foreach (char c in incoming)
        {
            if (char.IsControl(c) || _promptBuffer.Length >= _promptCapacity)
            {
                continue;
            }

            _promptBuffer += c;
        }
    }

    private void RefreshPixelSize()
    {
        if (_window == null)
        {
            return;
        }

        int width = Width;
        int height = Height;
        if (SDL3.SDL_GetWindowSizeInPixels(_window, &width, &height))
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }
    }

    private static bool TryMapButton(byte button, out MouseButton mapped)
    {
        switch (button)
        {
            case 1:
                mapped = MouseButton.Left;
                return true;
            case 2:
                mapped = MouseButton.Middle;
                return true;
            case 3:
                mapped = MouseButton.Right;
                return true;
            default:
                mapped = default;
                return false;
        }
    }

    private static bool TryMapScancode(SDL_Scancode scancode, out HostKey key)
    {
        int code = (int)scancode;
        key = code switch
        {
            >= 4 and <= 29 => (HostKey)((int)HostKey.A + (code - 4)),
            30 => HostKey.D1,
            31 => HostKey.D2,
            32 => HostKey.D3,
            33 => HostKey.D4,
            34 => HostKey.D5,
            35 => HostKey.D6,
            36 => HostKey.D7,
            37 => HostKey.D8,
            38 => HostKey.D9,
            39 => HostKey.D0,
            40 or 88 => HostKey.Enter,
            41 => HostKey.Escape,
            42 => HostKey.Backspace,
            43 => HostKey.Tab,
            44 => HostKey.Space,
            45 => HostKey.Minus,
            46 => HostKey.Plus,
            47 => HostKey.LeftBracket,
            48 => HostKey.RightBracket,
            52 => HostKey.Quote,
            54 => HostKey.Comma,
            55 => HostKey.Period,
            >= 58 and <= 69 => (HostKey)((int)HostKey.F1 + (code - 58)),
            72 => HostKey.Pause,
            73 => HostKey.Insert,
            74 => HostKey.Home,
            75 => HostKey.PageUp,
            76 => HostKey.Delete,
            77 => HostKey.End,
            78 => HostKey.PageDown,
            79 => HostKey.Right,
            80 => HostKey.Left,
            81 => HostKey.Down,
            82 => HostKey.Up,
            86 => HostKey.NumpadMinus,
            87 => HostKey.NumpadPlus,
            98 => HostKey.Numpad0,
            >= 89 and <= 97 => (HostKey)((int)HostKey.Numpad1 + (code - 89)),
            224 => HostKey.LeftControl,
            225 => HostKey.LeftShift,
            226 => HostKey.LeftAlt,
            228 => HostKey.RightControl,
            229 => HostKey.RightShift,
            230 => HostKey.RightAlt,
            _ => (HostKey)(-1),
        };
        return (int)key >= 0;
    }
}
