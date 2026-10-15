// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;
using System.Numerics;
using OpenVirtue.Engine.Interpreter;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Host;

public sealed partial class RuntimeHost
{
    private void DispatchInputEdges()
    {
        UpdateMouseRuntimeState(_input.PointerPosition);
        foreach (HostKey key in _input.Pressed)
        {
            UpdateRuntimeInputSkills();
            TriggerRuntimeKeyHooks(key);
        }

        if (_input.WasPressed(MouseButton.Left))
        {
            HandleMouseDown(MouseButton.Left);
        }

        if (_input.WasPressed(MouseButton.Right))
        {
            HandleMouseDown(MouseButton.Right);
        }

        if (_input.WasPressed(MouseButton.Middle))
        {
            HandleMouseDown(MouseButton.Middle);
        }

        if (_input.WasReleased(MouseButton.Left))
        {
            HandleMouseUp();
        }
        else if (_input.WasReleased(MouseButton.Right) || _input.WasReleased(MouseButton.Middle))
        {
            UpdateRuntimeInputSkills();
        }

        if (_draggedUiSlider is not null)
        {
            UpdateDraggedUiSlider(_input.PointerPosition);
        }
    }

    private void HandleMouseDown(MouseButton button)
    {
        UpdateMouseRuntimeState(_input.PointerPosition);
        UpdateRuntimeInputSkills();
        if (button == MouseButton.Left)
        {
            TriggerRuntimeMouseHook("IF_LEFT");
            if (!TryTriggerUiKlick(_input.PointerPosition) && !TryTriggerSpriteKlick(_input.PointerPosition))
            {
                TriggerRuntimeMouseHook("IF_KLICK");
            }
        }
        else if (button == MouseButton.Right)
        {
            TriggerRuntimeMouseHook("IF_RIGHT");
        }
        else if (button == MouseButton.Middle)
        {
            TriggerRuntimeMouseHook("IF_MIDDLE");
        }
    }

    private void HandleMouseUp()
    {
        UpdateMouseRuntimeState(_input.PointerPosition);
        UpdateRuntimeInputSkills();
        if (_draggedUiSlider is not null)
        {
            UpdateDraggedUiSlider(_input.PointerPosition);
            _draggedUiSlider = null;
            _sliderGrabOffset = 0;
        }

        if (_pressedUiButton is null)
        {
            return;
        }

        string? releaseAction = _pressedUiButtonReleaseAction;
        _pressedUiButton = null;
        _pressedUiButtonReleaseAction = null;
        if (releaseAction is null || _handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.RunAction(releaseAction);
        HandleRuntimeHostRequests();
    }

    private void TriggerRuntimeKeyHooks(HostKey key)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.TriggerActionHook("IF_ANYKEY");
        string? hookName = KeyHookName(key);
        if (hookName is not null)
        {
            _runtime.TriggerActionHook(hookName);
        }

        HandleRuntimeHostRequests();
    }

    private static string? KeyHookName(HostKey key)
    {
        if (key >= HostKey.F1 && key <= HostKey.F12)
        {
            return "IF_F" + ((int)key - (int)HostKey.F1 + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (key >= HostKey.A && key <= HostKey.Z)
        {
            return "IF_" + (char)('A' + (int)key - (int)HostKey.A);
        }

        if (key >= HostKey.D0 && key <= HostKey.D9)
        {
            return "IF_" + ((int)key - (int)HostKey.D0).ToString(CultureInfo.InvariantCulture);
        }

        if (key >= HostKey.Numpad0 && key <= HostKey.Numpad9)
        {
            return "IF_" + ((int)key - (int)HostKey.Numpad0).ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            HostKey.Escape => "IF_ESC",
            HostKey.Enter => "IF_ENTER",
            HostKey.Space => "IF_SPACE",
            HostKey.Tab => "IF_TAB",
            HostKey.Backspace => "IF_BKSP",
            HostKey.LeftControl or HostKey.RightControl => "IF_CTRL",
            HostKey.Quote => "IF_APO",
            HostKey.Minus or HostKey.NumpadMinus => "IF_MINUS",
            HostKey.Plus or HostKey.NumpadPlus => "IF_PLUS",
            HostKey.LeftBracket => "IF_BRACKL",
            HostKey.RightBracket => "IF_BRACKR",
            HostKey.Up => "IF_CUU",
            HostKey.Down => "IF_CUD",
            HostKey.Left => "IF_CUL",
            HostKey.Right => "IF_CUR",
            HostKey.Insert => "IF_INS",
            HostKey.PageUp => "IF_PGUP",
            HostKey.PageDown => "IF_PGDN",
            _ => null,
        };
    }

    private void UpdateMouseRuntimeState(Vector2 physicalPoint)
    {
        if (_runtime is null || _runtime.GetSkill("MOUSE_MODE") > 0)
        {
            return;
        }

        Vector2 virtualPoint = VirtualPoint(physicalPoint);
        _runtime.SetSkill("MOUSE_X", virtualPoint.X);
        _runtime.SetSkill("MOUSE_Y", virtualPoint.Y);
    }

    private void TriggerRuntimeMouseHook(string hookName)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return;
        }

        SyncPlayerRuntimeState();
        _runtime.TriggerActionHook(hookName);
        HandleRuntimeHostRequests();
    }

    private bool TryTriggerUiKlick(Vector2 point)
    {
        if (_handlingInputRequest || _runtime is null)
        {
            return false;
        }

        Dictionary<string, UiBitmapSize> bitmapSizes = BitmapSizes();
        Vector2 virtualPoint = PointerVirtualPoint(point);
        UiHitTarget? target = UiHitTester.HitTest(
            _level,
            virtualPoint.X,
            virtualPoint.Y,
            bitmapSizes,
            _runtime.UiElements,
            _runtime.Skills,
            _runtime.Strings);
        if (target is not { } hit)
        {
            return false;
        }

        if (hit.Button is { } button)
        {
            _pressedUiButton = button;
            _pressedUiButtonReleaseAction = hit.ReleaseAction;
        }

        if (hit.Slider is { } slider)
        {
            _draggedUiSlider = slider;
            _sliderGrabOffset = slider.Vertical ? virtualPoint.Y - hit.Y : virtualPoint.X - hit.X;
        }

        if (hit.Action is { } action)
        {
            SyncPlayerRuntimeState();
            _runtime.RunAction(action);
            HandleRuntimeHostRequests();
        }

        return true;
    }

    private bool TryTriggerSpriteKlick(Vector2 point)
    {
        if (_handlingInputRequest || _runtime is null || _sprites.Count == 0)
        {
            return false;
        }

        Vector2 physical = PointerPhysicalPoint(point);
        VirtualDisplayLayout display = DisplayLayout();
        DisplayRectangle viewport = WorldViewport(display);
        ProjectionModel projection = CurrentProjection();
        float pixelScaleX = viewport.Width / projection.ScreenWidth;
        float pixelScaleY = viewport.Height / projection.ScreenHeight;
        Vector3 cameraPosition = _camera.Position;
        Vector3 forward = _camera.LevelForward;
        Vector3 right = _camera.Right;
        Vector3 up = Vector3.UnitY;
        IEnumerable<SpriteBillboard> clickableSprites = SpriteBuilder.SelectClickableLayers(_sprites)
            .OrderBy(sprite => Vector3.DistanceSquared(sprite.BasePosition, cameraPosition));
        foreach (SpriteBillboard sprite in clickableSprites)
        {
            if (sprite.Source is null ||
                !sprite.Source.TryGetString("IF_KLICK", out string? actionName) ||
                string.IsNullOrWhiteSpace(actionName))
            {
                continue;
            }

            Vector3 center = sprite.BasePosition +
                (right * sprite.CenterOffset.X) +
                (up * (sprite.HalfHeight + sprite.CenterOffset.Y));
            Vector3 toCenter = center - cameraPosition;
            float distance = Vector3.Dot(toCenter, forward);
            if (distance <= 1f)
            {
                continue;
            }

            float projectedX = Vector3.Dot(toCenter, right);
            float projectedY = Vector3.Dot(toCenter, up);
            (double windowX, double windowY) = projection.Project(distance, projectedX, projectedY);
            float screenX = viewport.X + ((float)windowX * pixelScaleX);
            float screenY = viewport.Y + ((float)windowY * pixelScaleY);
            float halfWidth = (float)(projection.FocalLengthX * sprite.HalfWidth / distance) * pixelScaleX;
            float halfHeight = (float)(projection.FocalLengthY * sprite.HalfHeight / distance) * pixelScaleY;
            if (physical.X < screenX - halfWidth ||
                physical.X > screenX + halfWidth ||
                physical.Y < screenY - halfHeight ||
                physical.Y > screenY + halfHeight ||
                !IsOpaqueSpritePixel(sprite, physical, screenX, screenY, halfWidth, halfHeight))
            {
                continue;
            }

            SyncPlayerRuntimeState();
            if (_runtime.TriggerObjectActionHook(sprite.Source, "IF_KLICK"))
            {
                HandleRuntimeHostRequests();
                return true;
            }
        }

        return false;
    }

    private bool IsOpaqueSpritePixel(
        SpriteBillboard sprite,
        Vector2 point,
        float screenX,
        float screenY,
        float halfWidth,
        float halfHeight)
    {
        if (!_textureImages.TryGetValue(sprite.Texture, out TextureImage image) || halfWidth <= 0 || halfHeight <= 0)
        {
            return true;
        }

        float u = (point.X - (screenX - halfWidth)) / (halfWidth * 2);
        float v = (point.Y - (screenY - halfHeight)) / (halfHeight * 2);
        return SpriteBuilder.IsOpaque(image, u, v, sprite.Mirror);
    }

    private void UpdateDraggedUiSlider(Vector2 point)
    {
        if (_draggedUiSlider is not { } slider || _runtime is null)
        {
            return;
        }

        Vector2 virtualPoint = VirtualPoint(point);
        float pointer = slider.Vertical ? virtualPoint.Y : virtualPoint.X;
        float start = slider.Vertical ? slider.StartY : slider.StartX;
        _runtime.SetSkill(slider.Skill, slider.ValueAtOffset(pointer - _sliderGrabOffset - start));
    }

    private void UpdateCamera()
    {
        RecordMouseMotion();
        UpdateRuntimeMouseMotion();
        UpdateRuntimeInputSkills(includeMouse: true);
        SyncPlayerRuntimeState();
        _camera.Position = RenderSpace.FromMap(_player.Position);
    }

    private void SyncPlayerRuntimeState()
    {
        if (_runtime is null || _player is null)
        {
            return;
        }

        Vector3 position = _player.Position;
        WdlPlayerMotion.Synchronize(_runtime, _player);
        double runtimeAngle = RenderSpace.PlayerAngleFromYaw(_camera.Yaw);
        _runtime.SetSkill("PLAYER_ANGLE", runtimeAngle);
        _runtime.SetSkill("PLAYER_SIN", Math.Sin(runtimeAngle));
        _runtime.SetSkill("PLAYER_COS", Math.Cos(runtimeAngle));
        double runtimeTilt = Math.Tan(_camera.Pitch);
        _runtime.SetSkill("PLAYER_TILT", runtimeTilt);
        _runtime.SetSkill("MOUSE_ANGLE", runtimeAngle);
        RememberPlayerRuntimeSnapshot(position.X, position.Z, position.Y, _player.Region, runtimeAngle, runtimeTilt);
    }

    private void UpdateRuntimeInputSkills(bool includeMouse = false)
    {
        if (_runtime is null)
        {
            return;
        }

        for (int i = 0; i < 12; i++)
        {
            SetRuntimeKeySkill("KEY_F" + (i + 1).ToString(CultureInfo.InvariantCulture), (HostKey)((int)HostKey.F1 + i));
        }

        for (int i = 0; i < 26; i++)
        {
            SetRuntimeKeySkill("KEY_" + (char)('A' + i), (HostKey)((int)HostKey.A + i));
        }

        for (int i = 0; i < 10; i++)
        {
            string name = "KEY_" + i.ToString(CultureInfo.InvariantCulture);
            bool down = _input.IsDown((HostKey)((int)HostKey.D0 + i)) ||
                _input.IsDown((HostKey)((int)HostKey.Numpad0 + i));
            _runtime.SetSkill(name, down ? 1 : 0);
        }

        SetRuntimeKeySkill("KEY_ESC", HostKey.Escape);
        SetRuntimeKeySkill("KEY_TAB", HostKey.Tab);
        SetRuntimeKeySkill("KEY_SHIFT", HostKey.LeftShift, HostKey.RightShift);
        SetRuntimeKeySkill("KEY_CTRL", HostKey.LeftControl, HostKey.RightControl);
        SetRuntimeKeySkill("KEY_ALT", HostKey.LeftAlt, HostKey.RightAlt);
        SetRuntimeKeySkill("KEY_SPACE", HostKey.Space);
        SetRuntimeKeySkill("KEY_BKSP", HostKey.Backspace);
        SetRuntimeKeySkill("KEY_CUU", HostKey.Up);
        SetRuntimeKeySkill("KEY_CUD", HostKey.Down);
        SetRuntimeKeySkill("KEY_CUR", HostKey.Right);
        SetRuntimeKeySkill("KEY_CUL", HostKey.Left);
        SetRuntimeKeySkill("KEY_PGUP", HostKey.PageUp);
        SetRuntimeKeySkill("KEY_PGDN", HostKey.PageDown);
        SetRuntimeKeySkill("KEY_HOME", HostKey.Home);
        SetRuntimeKeySkill("KEY_END", HostKey.End);
        SetRuntimeKeySkill("KEY_INS", HostKey.Insert);
        SetRuntimeKeySkill("KEY_DEL", HostKey.Delete);
        SetRuntimeKeySkill("KEY_PAUSE", HostKey.Pause);
        SetRuntimeKeySkill("KEY_PLUS", HostKey.Plus, HostKey.NumpadPlus);
        SetRuntimeKeySkill("KEY_MINUS", HostKey.Minus, HostKey.NumpadMinus);
        SetRuntimeKeySkill("KEY_ENTER", HostKey.Enter);
        _runtime.SetSkill("KEY_ANY", AnyKeyDown() ? 1 : 0);
        _runtime.SetSkill("MOUSE_LEFT", _input.IsDown(MouseButton.Left) ? 1 : 0);
        _runtime.SetSkill("MOUSE_MIDDLE", _input.IsDown(MouseButton.Middle) ? 1 : 0);
        _runtime.SetSkill("MOUSE_RIGHT", _input.IsDown(MouseButton.Right) ? 1 : 0);

        double sensitivity = _runtime.GetSkill("KEY_SENSE");
        if (IsDown(HostKey.LeftShift) || IsDown(HostKey.RightShift))
        {
            sensitivity *= _runtime.GetSkill("SHIFT_SENSE");
        }

        int ahead = (IsDown(HostKey.Up) || IsDown(HostKey.W) ? 1 : 0) - (IsDown(HostKey.Down) || IsDown(HostKey.S) ? 1 : 0);
        int strafe = (IsDown(HostKey.Period) || IsDown(HostKey.D) ? 1 : 0) - (IsDown(HostKey.Comma) || IsDown(HostKey.A) ? 1 : 0);
        int vertical = (IsDown(HostKey.Home) || IsDown(HostKey.Space) ? 1 : 0) - (IsDown(HostKey.End) ? 1 : 0);
        double mouseRotation = 0;
        double mouseTilt = 0;
        if (includeMouse)
        {
            double mouseSensitivity = _runtime.GetSkill("MOUSE_SENSE");
            double mouseMode = _runtime.GetSkill("MOUSE_MODE");
            // Cursor-left is positive FORCE_ROT (PLAYER_ANGLE grows, a left turn). A rightward
            // mickey is therefore negative. Positive PLAYER_TILT looks down, so a forward
            // mickey (negative Y) is a negative FORCE_TILT.
            mouseRotation = WdlInputForces.MouseAxis(-_frameMickey.X, mouseSensitivity, mouseMode);
            mouseTilt = WdlInputForces.MouseAxis(_frameMickey.Y, mouseSensitivity, mouseMode);
        }

        _runtime.SetSkill("FORCE_AHEAD", sensitivity * ahead);
        _runtime.SetSkill("FORCE_STRAFE", sensitivity * strafe);
        _runtime.SetSkill("FORCE_ROT", WdlInputForces.Combine(sensitivity * Axis(HostKey.Left, HostKey.Right), mouseRotation));
        _runtime.SetSkill("FORCE_TILT", WdlInputForces.Combine(sensitivity * Axis(HostKey.PageDown, HostKey.PageUp), mouseTilt));
        _runtime.SetSkill("FORCE_UP", sensitivity * vertical);
    }

    private void RecordMouseMotion()
    {
        Vector2 physical = _input.ConsumeMotion();
        VirtualDisplayLayout layout = DisplayLayout();
        _pendingMickey += new Vector2(
            physical.X / Math.Max(0.001f, layout.ScaleX),
            physical.Y / Math.Max(0.001f, layout.ScaleY));
    }

    private void UpdateRuntimeMouseMotion()
    {
        _frameMickey = _pendingMickey;
        _pendingMickey = Vector2.Zero;
        _runtime.SetSkill("MICKEY_X", _frameMickey.X);
        _runtime.SetSkill("MICKEY_Y", _frameMickey.Y);
        _mouseMotionWindow += _frameMickey;
        _mouseMotionTicks += Math.Max(0, _lastTimeCorrection);
        double calm = Math.Max(0, _runtime.GetSkill("MOUSE_CALM"));
        double sampleTicks = Math.Max(0.001, _runtime.GetSkill("MOUSE_TIME"));
        bool moving = _mouseMotionWindow.Length() > calm;
        if (moving)
        {
            _mouseMotionWindow = Vector2.Zero;
            _mouseMotionTicks = 0;
        }
        else if (_mouseMotionTicks < sampleTicks)
        {
            moving = _mouseWasMoving;
        }
        else
        {
            _mouseMotionWindow = Vector2.Zero;
            _mouseMotionTicks = 0;
        }

        _runtime.SetSkill("MOUSE_MOVING", moving ? 1 : 0);
        if (_mouseWasMoving && !moving && _runtime.GetSkill("MOUSE_MODE") > 0)
        {
            _runtime.TriggerActionHook("IF_MSTOP");
            HandleRuntimeHostRequests();
        }

        _mouseWasMoving = moving;
    }

    private Vector2 PointerVirtualPoint(Vector2 physicalPoint) =>
        _runtime is not null && _runtime.GetSkill("MOUSE_MODE") > 0
            ? new Vector2((float)_runtime.GetSkill("MOUSE_X"), (float)_runtime.GetSkill("MOUSE_Y"))
            : VirtualPoint(physicalPoint);

    private Vector2 PointerPhysicalPoint(Vector2 physicalPoint)
    {
        if (_runtime is null || _runtime.GetSkill("MOUSE_MODE") <= 0)
        {
            return physicalPoint;
        }

        VirtualDisplayLayout display = DisplayLayout();
        DisplayRectangle point = display.ToPhysical(
            (float)_runtime.GetSkill("MOUSE_X"),
            (float)_runtime.GetSkill("MOUSE_Y"),
            0,
            0);
        return new Vector2(point.X, point.Y);
    }

    private Vector2 VirtualPoint(Vector2 point) => DisplayLayout().ToVirtual(point.X, point.Y);

    private void SetRuntimeKeySkill(string name, params HostKey[] keys) =>
        _runtime.SetSkill(name, keys.Any(_input.IsDown) ? 1 : 0);

    private bool IsDown(HostKey key) => _input.IsDown(key);

    private bool AnyKeyDown()
    {
        foreach (HostKey key in Enum.GetValues<HostKey>())
        {
            if (_input.IsDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private int Axis(HostKey positive, HostKey negative) =>
        (IsDown(positive) ? 1 : 0) - (IsDown(negative) ? 1 : 0);

    private void ApplyRuntimePlayerState()
    {
        if (_runtime is null || _player is null)
        {
            return;
        }

        if (!_hasPlayerRuntimeSnapshot)
        {
            SyncPlayerRuntimeState();
            return;
        }

        double playerX = _runtime.GetSkill("PLAYER_X");
        double playerY = _runtime.GetSkill("PLAYER_Y");
        double playerZ = _runtime.GetSkill("PLAYER_Z");
        double playerRegion = _runtime.GetSkill("PLAYER_REGION");
        double playerAngle = NormalizeAngle(_runtime.GetSkill("PLAYER_ANGLE"));
        double playerTilt = _runtime.GetSkill("PLAYER_TILT");
        bool positionChanged =
            !NearlyEqual(playerX, _lastPlayerX) ||
            !NearlyEqual(playerY, _lastPlayerY) ||
            !NearlyEqual(playerZ, _lastPlayerZ) ||
            !NearlyEqual(playerRegion, _lastPlayerRegion);
        bool angleChanged = !NearlyEqual(playerAngle, _lastPlayerAngle);
        bool tiltChanged = !NearlyEqual(playerTilt, _lastPlayerTilt);
        if (positionChanged)
        {
            _player["region"] = Math.Round(playerRegion);
            _player.Position = new Vector3((float)playerX, (float)playerZ, (float)playerY);
        }

        if (angleChanged)
        {
            _camera.Yaw = (float)RenderSpace.YawFromPlayerAngle(playerAngle);
        }

        if (tiltChanged)
        {
            _camera.Pitch = (float)Math.Atan(playerTilt);
        }

        _camera.Position = RenderSpace.FromMap(_player.Position);
        SyncPlayerRuntimeState();
    }

    private void RememberPlayerRuntimeSnapshot(double x, double y, double z, double region, double angle, double tilt)
    {
        _hasPlayerRuntimeSnapshot = true;
        _lastPlayerX = x;
        _lastPlayerY = y;
        _lastPlayerZ = z;
        _lastPlayerRegion = region;
        _lastPlayerAngle = NormalizeAngle(angle);
        _lastPlayerTilt = tilt;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= FullTurnRadians;
        return angle < 0 ? angle + FullTurnRadians : angle;
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;

    private Dictionary<string, UiBitmapSize> BitmapSizes() =>
        _uiImages.ToDictionary(
            pair => pair.Key,
            pair => new UiBitmapSize(pair.Value.Width, pair.Value.Height),
            StringComparer.OrdinalIgnoreCase);
}
