// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A screen-space bitmap UI element resolved from WDL overlay/panel declarations.</summary>
public readonly record struct UiOverlaySprite(
    string SourceElement,
    string Bitmap,
    float X,
    float Y,
    int Width,
    int Height,
    int Layer,
    bool Absolute,
    int SourceX = 0,
    int SourceY = 0,
    int SourceWidth = 0,
    int SourceHeight = 0);

/// <summary>
/// Resolves the first renderable UI bitmap layer from captured WDL UI declarations.
/// Text and some panel widgets are handled by companion UI builders.
/// </summary>
public static class UiOverlayBuilder
{
    /// <summary>Builds visible bitmap overlays and panel backgrounds in ascending layer order.</summary>
    public static IReadOnlyList<UiOverlaySprite> Build(
        Level level,
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = null,
        IReadOnlyDictionary<string, double>? skills = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        IReadOnlyDictionary<string, WdlUiElement> elements = uiElements ?? level.UiElements;
        HashSet<string> mouseSprites = elements.Values
            .Where(element => element.Keyword.Equals("MSPRITE", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, double> liveSkills = skills ?? level.Skills;
        var sprites = new List<UiOverlaySprite>();
        foreach (WdlUiElement element in elements.Values)
        {
            if (element.Keyword.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase))
            {
                if (mouseSprites.Contains(element.Name))
                {
                    AddMouseSprite(level, element, liveSkills, sprites);
                }
                else
                {
                    AddOverlay(level, element, sprites);
                }
            }
            else if (element.Keyword.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
            {
                AddPanelBackground(level, element, sprites);
            }
        }

        return sprites
            .OrderBy(sprite => sprite.Layer)
            .ThenBy(sprite => sprite.SourceElement, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddMouseSprite(
        Level level,
        WdlUiElement element,
        IReadOnlyDictionary<string, double> skills,
        List<UiOverlaySprite> sprites)
    {
        if (skills.GetValueOrDefault("MOUSE_MODE") <= 0 ||
            FirstPropertyValue(element, "BMAP", "OVLYS", "OVLY") is not { } bitmapName ||
            !level.Bitmaps.TryGetValue(bitmapName, out LevelBitmap bitmap))
        {
            return;
        }

        float hotspotX = NumberProperty(element, "POS_X") ?? 0;
        float hotspotY = NumberProperty(element, "POS_Y") ?? 0;
        sprites.Add(new UiOverlaySprite(
            element.Name,
            bitmapName,
            (float)skills.GetValueOrDefault("MOUSE_X") - hotspotX,
            (float)skills.GetValueOrDefault("MOUSE_Y") - hotspotY,
            bitmap.Width,
            bitmap.Height,
            (int)(NumberProperty(element, "LAYER") ?? 0),
            true));
    }

    /// <summary>Builds visible panel bitmap widgets, including buttons, sliders, windows, and pictures.</summary>
    public static IReadOnlyList<UiOverlaySprite> BuildPanelWidgets(
        Level level,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes = null,
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = null,
        UiButtonIdentity? pressedButton = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(skills);

        var sprites = new List<UiOverlaySprite>();
        foreach (WdlUiElement element in (uiElements ?? level.UiElements).Values)
        {
            if (element.Keyword.Equals("PANEL", StringComparison.OrdinalIgnoreCase) && HasFlag(element, "VISIBLE"))
            {
                AddPanelButtons(level, element, bitmapSizes, pressedButton, sprites);
                AddPanelSliders(level, element, skills, bitmapSizes, sprites);
                AddPanelBars(level, element, skills, bitmapSizes, sprites);
                AddPanelWindows(element, skills, bitmapSizes, sprites);
                AddPanelPictures(level, element, skills, sprites);
            }
        }

        return sprites
            .OrderBy(sprite => sprite.Layer)
            .ThenBy(sprite => sprite.SourceElement, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Builds visible panel <c>WINDOW</c> cutouts from live skill values.</summary>
    public static IReadOnlyList<UiOverlaySprite> BuildPanelWindows(
        Level level,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes = null) =>
        BuildPanelWidgets(level, skills, bitmapSizes)
            .Where(sprite => sprite.SourceWidth > 0 && sprite.SourceHeight > 0)
            .ToArray();

    private static void AddPanelButtons(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        UiButtonIdentity? pressedButton,
        List<UiOverlaySprite> sprites)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        int buttonIndex = 0;
        foreach (WdlUiProperty property in panel.Properties)
        {
            if (!property.Keyword.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WdlUiValue[] values = ArgumentValues(property.Values);
            int currentButtonIndex = buttonIndex++;
            int bitmapIndex = pressedButton == new UiButtonIdentity(panel.Name, currentButtonIndex) ? 2 : 3;
            if (values.Length <= bitmapIndex ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                IdentifierAt(values, bitmapIndex) is not { } bitmapName ||
                bitmapName.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
                ResolveBitmapSize(level, bitmapSizes, bitmapName) is not { } size ||
                size.Width <= 0 ||
                size.Height <= 0)
            {
                continue;
            }

            sprites.Add(new UiOverlaySprite(
                panel.Name,
                bitmapName,
                panelX + localX,
                panelY + localY,
                size.Width,
                size.Height,
                layer,
                true));
        }
    }

    private static void AddPanelSliders(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        List<UiOverlaySprite> sprites)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        foreach (WdlUiProperty property in panel.Properties)
        {
            bool vertical = property.Keyword.Equals("VSLIDER", StringComparison.OrdinalIgnoreCase);
            if (!vertical && !property.Keyword.Equals("HSLIDER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WdlUiValue[] values = ArgumentValues(property.Values);
            if (values.Length < 5 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                NumberAt(values, 2) is not { } length ||
                length <= 0 ||
                IdentifierAt(values, 3) is not { } bitmapName ||
                IdentifierAt(values, 4) is not { } skillName ||
                !level.SkillBounds.TryGetValue(skillName, out SkillRange range) ||
                !double.IsFinite(range.Min) ||
                !double.IsFinite(range.Max) ||
                range.Max <= range.Min ||
                ResolveBitmapSize(level, bitmapSizes, bitmapName) is not { } size ||
                size.Width <= 0 ||
                size.Height <= 0)
            {
                continue;
            }

            var slider = new UiSliderTarget(skillName, vertical, panelX + localX, panelY + localY, length, range.Min, range.Max);
            double value = skills.GetValueOrDefault(skillName, range.Min);
            float offset = slider.OffsetFor(value);
            sprites.Add(new UiOverlaySprite(
                panel.Name,
                bitmapName,
                panelX + localX + (vertical ? 0 : offset),
                panelY + localY + (vertical ? offset : 0),
                size.Width,
                size.Height,
                layer,
                true));
        }
    }

    private static void AddPanelBars(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        List<UiOverlaySprite> sprites)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        foreach (WdlUiProperty property in panel.Properties)
        {
            bool vertical = property.Keyword.Equals("VBAR", StringComparison.OrdinalIgnoreCase);
            if (!vertical && !property.Keyword.Equals("HBAR", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WdlUiValue[] values = ArgumentValues(property.Values);
            if (values.Length < 6 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                NumberAt(values, 2) is not { } lengthValue ||
                IdentifierAt(values, 3) is not { } bitmapName ||
                NumberAt(values, 4) is not { } factor ||
                IdentifierAt(values, 5) is not { } skillName ||
                ResolveBitmapSize(level, bitmapSizes, bitmapName) is not { } bitmapSize)
            {
                continue;
            }

            int length = Math.Max(0, (int)Math.Truncate(lengthValue));
            int width = vertical ? bitmapSize.Width : Math.Min(length, bitmapSize.Width);
            int height = vertical ? Math.Min(length, bitmapSize.Height) : bitmapSize.Height;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            int sourceOffset = Math.Max(0, (int)Math.Truncate(factor * skills.GetValueOrDefault(skillName)));
            int sourceX = vertical ? 0 : Math.Clamp(sourceOffset, 0, Math.Max(0, bitmapSize.Width - width));
            int sourceY = vertical ? Math.Clamp(sourceOffset, 0, Math.Max(0, bitmapSize.Height - height)) : 0;
            sprites.Add(new UiOverlaySprite(
                panel.Name,
                bitmapName,
                panelX + localX,
                panelY + localY,
                width,
                height,
                layer,
                true,
                sourceX,
                sourceY,
                width,
                height));
        }
    }

    private static void AddOverlay(Level level, WdlUiElement element, List<UiOverlaySprite> sprites)
    {
        if (!HasFlag(element, "VISIBLE") ||
            FirstPropertyValue(element, "BMAP", "OVLYS", "OVLY") is not { } bitmapName ||
            !level.Bitmaps.TryGetValue(bitmapName, out LevelBitmap bitmap))
        {
            return;
        }

        sprites.Add(new UiOverlaySprite(
            element.Name,
            bitmapName,
            NumberProperty(element, "POS_X") ?? 0,
            NumberProperty(element, "POS_Y") ?? 0,
            bitmap.Width,
            bitmap.Height,
            (int)(NumberProperty(element, "LAYER") ?? 0),
            HasFlag(element, "ABSPOS")));
    }

    private static void AddPanelBackground(Level level, WdlUiElement element, List<UiOverlaySprite> sprites)
    {
        if (!HasFlag(element, "VISIBLE") ||
            FirstPropertyValue(element, "PAN_MAP") is not { } bitmapName ||
            !level.Bitmaps.TryGetValue(bitmapName, out LevelBitmap bitmap))
        {
            return;
        }

        sprites.Add(new UiOverlaySprite(
            element.Name,
            bitmapName,
            NumberProperty(element, "POS_X") ?? 0,
            NumberProperty(element, "POS_Y") ?? 0,
            bitmap.Width,
            bitmap.Height,
            (int)(NumberProperty(element, "LAYER") ?? 0),
            !HasFlag(element, "RELPOS")));
    }

    private static void AddPanelWindows(
        WdlUiElement panel,
        IReadOnlyDictionary<string, double> skills,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        List<UiOverlaySprite> sprites)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        foreach (WdlUiProperty property in panel.Properties)
        {
            WdlUiValue[] values = ArgumentValues(property.Values);
            if (!property.Keyword.Equals("WINDOW", StringComparison.OrdinalIgnoreCase) ||
                values.Length < 7 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                NumberAt(values, 2) is not { } widthValue ||
                NumberAt(values, 3) is not { } heightValue ||
                IdentifierAt(values, 4) is not { } bitmapName ||
                IdentifierAt(values, 5) is not { } skillXName ||
                IdentifierAt(values, 6) is not { } skillYName)
            {
                continue;
            }

            int width = Math.Max(0, (int)Math.Truncate(widthValue));
            int height = Math.Max(0, (int)Math.Truncate(heightValue));
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            int sourceX = Math.Max(0, (int)Math.Truncate(skills.GetValueOrDefault(skillXName)));
            int sourceY = Math.Max(0, (int)Math.Truncate(skills.GetValueOrDefault(skillYName)));
            if (bitmapSizes is not null &&
                bitmapSizes.TryGetValue(bitmapName, out UiBitmapSize bitmapSize))
            {
                sourceX = Math.Clamp(sourceX, 0, Math.Max(0, bitmapSize.Width - width));
                sourceY = Math.Clamp(sourceY, 0, Math.Max(0, bitmapSize.Height - height));
            }

            sprites.Add(new UiOverlaySprite(
                panel.Name,
                bitmapName,
                panelX + localX,
                panelY + localY,
                width,
                height,
                layer,
                true,
                sourceX,
                sourceY,
                width,
                height));
        }
    }

    private static void AddPanelPictures(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, double> skills,
        List<UiOverlaySprite> sprites)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        foreach (WdlUiProperty property in panel.Properties)
        {
            WdlUiValue[] values = ArgumentValues(property.Values);
            if (!property.Keyword.Equals("PICTURE", StringComparison.OrdinalIgnoreCase) ||
                values.Length < 4 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                IdentifierAt(values, 2) is not { } textureName ||
                IdentifierAt(values, 3) is not { } skillName ||
                !level.Textures.TryGetValue(textureName, out LevelTexture texture) ||
                texture.Frames.Count == 0)
            {
                continue;
            }

            int frameIndex = Math.Max(0, (int)Math.Truncate(skills.GetValueOrDefault(skillName)) - 1);
            frameIndex = Math.Clamp(frameIndex, 0, texture.Frames.Count - 1);
            if (texture.Frames[frameIndex] is not { } frame)
            {
                continue;
            }

            sprites.Add(new UiOverlaySprite(
                panel.Name,
                frame.Name,
                panelX + localX,
                panelY + localY,
                frame.Width,
                frame.Height,
                layer,
                true));
        }
    }

    private static UiBitmapSize? ResolveBitmapSize(
        Level level,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        string bitmapName)
    {
        if (level.Bitmaps.TryGetValue(bitmapName, out LevelBitmap bitmap) &&
            bitmap.Width > 0 &&
            bitmap.Height > 0)
        {
            return new UiBitmapSize(bitmap.Width, bitmap.Height);
        }

        return bitmapSizes is not null && bitmapSizes.TryGetValue(bitmapName, out UiBitmapSize size)
            ? size
            : null;
    }

    private static bool HasFlag(WdlUiElement element, string flag) =>
        element.Properties.Any(property =>
            property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) &&
            property.Values.Any(value => value.Text.Equals(flag, StringComparison.OrdinalIgnoreCase)));

    private static string? FirstPropertyValue(WdlUiElement element, params string[] names)
    {
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!names.Any(name => property.Keyword.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                property.Values.Count == 0)
            {
                continue;
            }

            return property.Values[0].Text;
        }

        return null;
    }

    private static float? NumberProperty(WdlUiElement element, string name)
    {
        foreach (WdlUiProperty property in element.Properties)
        {
            if (property.Keyword.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return FirstNumber(property.Values);
            }
        }

        return null;
    }

    private static float? FirstNumber(IReadOnlyList<WdlUiValue> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        int sign = 1;
        int index = 0;
        if (values.Count > 1 &&
            values[0].Kind.Equals("Operator", StringComparison.OrdinalIgnoreCase) &&
            (values[0].Text == "-" || values[0].Text == "+"))
        {
            sign = values[0].Text == "-" ? -1 : 1;
            index = 1;
        }

        return values[index].Kind.Equals("Number", StringComparison.OrdinalIgnoreCase) &&
               float.TryParse(values[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? sign * value
            : null;
    }

    private static float? NumberAt(WdlUiValue[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return null;
        }

        return values[index].Kind.Equals("Number", StringComparison.OrdinalIgnoreCase) &&
               float.TryParse(values[index].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : null;
    }

    private static string? IdentifierAt(WdlUiValue[] values, int index)
    {
        if (index < 0 || index >= values.Length)
        {
            return null;
        }

        return values[index].Kind.Equals("Identifier", StringComparison.OrdinalIgnoreCase)
            ? values[index].Text
            : null;
    }

    private static WdlUiValue[] ArgumentValues(IReadOnlyList<WdlUiValue> values) =>
        values
            .Where(value => !value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
