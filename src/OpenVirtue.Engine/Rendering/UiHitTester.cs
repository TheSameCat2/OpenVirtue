// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;

namespace OpenVirtue.Engine.Rendering;

/// <summary>The pixel size of a decoded UI bitmap resource.</summary>
public readonly record struct UiBitmapSize(int Width, int Height);

/// <summary>Stable identity of a button within a panel declaration.</summary>
public readonly record struct UiButtonIdentity(string Panel, int Index);

/// <summary>The value mapping and orientation of a draggable panel slider.</summary>
public readonly record struct UiSliderTarget(
    string Skill,
    bool Vertical,
    float StartX,
    float StartY,
    float Length,
    double Min,
    double Max,
    float KnobSize = 0)
{
    /// <summary>
    /// How far the knob travels: the slider's length less the knob's own width (height for a
    /// <c>VSLIDER</c>). A 40-pixel slider with a 4-pixel knob put it 36 pixels on at <c>MAX</c>
    /// (OV-20261029-ui-widgets, round 2).
    /// </summary>
    public float Travel => Math.Max(0, Length - KnobSize);

    /// <summary>
    /// Returns the knob's offset for a skill value: (value − MIN)/(MAX − MIN) of the travel, cut to
    /// a whole pixel (22.5 drew at 22), MIN at the left or top.
    /// </summary>
    public float OffsetFor(double value) =>
        MathF.Floor((float)(Math.Clamp((value - Min) / (Max - Min), 0, 1) * Travel));

    /// <summary>Returns the bounded skill value represented by a thumb offset.</summary>
    public double ValueAtOffset(double offset) =>
        Travel <= 0 ? Min : Min + (Math.Clamp(offset / Travel, 0, 1) * (Max - Min));
}

/// <summary>A screen-space UI region with a WDL click action.</summary>
public readonly record struct UiHitTarget(
    string SourceElement,
    string ElementKind,
    string? Action,
    float X,
    float Y,
    int Width,
    int Height,
    int Layer,
    string TargetKind = "Element",
    UiButtonIdentity? Button = null,
    string? ReleaseAction = null,
    UiSliderTarget? Slider = null);

/// <summary>Resolves first-pass clickable UI regions from visible panel/overlay declarations.</summary>
public static class UiHitTester
{
    /// <summary>Returns the topmost hit target at the given screen-space pixel position, if any.</summary>
    public static UiHitTarget? HitTest(
        Level level,
        float x,
        float y,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes = null,
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = null,
        IReadOnlyDictionary<string, double>? skills = null,
        IReadOnlyDictionary<string, WdlString>? strings = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        foreach (UiHitTarget target in BuildTargets(level, bitmapSizes, uiElements, skills, strings)
                     .OrderByDescending(target => target.Layer))
        {
            if (x >= target.X &&
                x < target.X + target.Width &&
                y >= target.Y &&
                y < target.Y + target.Height)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>Builds visible panel/overlay click targets in declaration order.</summary>
    public static IReadOnlyList<UiHitTarget> BuildTargets(
        Level level,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes = null,
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = null,
        IReadOnlyDictionary<string, double>? skills = null,
        IReadOnlyDictionary<string, WdlString>? strings = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        var targets = new List<UiHitTarget>();
        IReadOnlyDictionary<string, WdlUiElement> elements = uiElements ?? level.UiElements;
        foreach (WdlUiElement element in elements.Values)
        {
            if (element.Keyword.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
            {
                AddPanelButtonTargets(level, element, bitmapSizes, targets);
                AddPanelSliderTargets(level, element, bitmapSizes, skills ?? level.Skills, targets);
                AddTarget(level, element, "PAN_MAP", bitmapSizes, targets);
            }
            else if (element.Keyword.Equals("OVERLAY", StringComparison.OrdinalIgnoreCase))
            {
                AddTarget(level, element, "BMAP", bitmapSizes, targets);
            }
        }

        AddTextTargets(level, elements, strings ?? level.Strings, skills ?? level.Skills, targets);

        return targets;
    }

    private static void AddTextTargets(
        Level level,
        IReadOnlyDictionary<string, WdlUiElement> elements,
        IReadOnlyDictionary<string, WdlString> strings,
        IReadOnlyDictionary<string, double> skills,
        List<UiHitTarget> targets)
    {
        foreach (IGrouping<string, UiTextGlyph> group in UiTextBuilder
                     .Build(level, strings, skills, elements)
                     .GroupBy(glyph => glyph.SourceElement, StringComparer.OrdinalIgnoreCase))
        {
            if (!elements.TryGetValue(group.Key, out WdlUiElement? element) ||
                !element.Keyword.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
                FirstPropertyValue(element, "IF_KLICK") is not { } action ||
                string.IsNullOrWhiteSpace(action))
            {
                continue;
            }

            UiTextGlyph[] glyphs = group.ToArray();
            float left = glyphs.Min(glyph => glyph.X);
            float top = glyphs.Min(glyph => glyph.Y);
            float right = glyphs.Max(glyph => glyph.X + (glyph.DrawWidth > 0 ? glyph.DrawWidth : glyph.Width));
            float bottom = glyphs.Max(glyph => glyph.Y + (glyph.DrawHeight > 0 ? glyph.DrawHeight : glyph.Height));
            int width = Math.Max(1, (int)Math.Ceiling(right - left));
            int height = Math.Max(1, (int)Math.Ceiling(bottom - top));
            targets.Add(new UiHitTarget(
                element.Name,
                element.Keyword,
                action,
                left,
                top,
                width,
                height,
                glyphs.Max(glyph => glyph.Layer),
                "TEXT"));
        }
    }

    private static void AddTarget(
        Level level,
        WdlUiElement element,
        string primaryBitmapProperty,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        List<UiHitTarget> targets)
    {
        if (!HasFlag(element, "VISIBLE") ||
            FirstPropertyValue(element, "IF_KLICK") is not { } action ||
            string.IsNullOrWhiteSpace(action) ||
            FirstPropertyValue(element, primaryBitmapProperty, "OVLYS", "OVLY") is not { } bitmapName)
        {
            return;
        }

        int width = 0;
        int height = 0;
        if (level.Bitmaps.TryGetValue(bitmapName, out LevelBitmap bitmap))
        {
            width = bitmap.Width;
            height = bitmap.Height;
        }

        if ((width <= 0 || height <= 0) &&
            bitmapSizes is not null &&
            bitmapSizes.TryGetValue(bitmapName, out UiBitmapSize size))
        {
            width = size.Width;
            height = size.Height;
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        targets.Add(new UiHitTarget(
            element.Name,
            element.Keyword,
            action,
            NumberProperty(element, "POS_X") ?? 0,
            NumberProperty(element, "POS_Y") ?? 0,
            width,
            height,
            (int)(NumberProperty(element, "LAYER") ?? 0)));
    }

    private static void AddPanelButtonTargets(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        List<UiHitTarget> targets)
    {
        if (!HasFlag(panel, "VISIBLE"))
        {
            return;
        }

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
            if (values.Length < 4 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                IdentifierAt(values, 2) is not { } bitmapName ||
                ResolveBitmapSize(level, bitmapSizes, bitmapName) is not { } size ||
                size.Width <= 0 ||
                size.Height <= 0)
            {
                continue;
            }

            targets.Add(new UiHitTarget(
                panel.Name,
                panel.Keyword,
                ActionAt(values, 4),
                panelX + localX,
                panelY + localY,
                size.Width,
                size.Height,
                layer + 1,
                "BUTTON",
                new UiButtonIdentity(panel.Name, currentButtonIndex),
                ActionAt(values, 5)));
        }
    }

    private static string? ActionAt(WdlUiValue[] values, int index)
    {
        string? action = IdentifierAt(values, index);
        return string.IsNullOrWhiteSpace(action) || action.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            ? null
            : action;
    }

    private static void AddPanelSliderTargets(
        Level level,
        WdlUiElement panel,
        IReadOnlyDictionary<string, UiBitmapSize>? bitmapSizes,
        IReadOnlyDictionary<string, double> skills,
        List<UiHitTarget> targets)
    {
        if (!HasFlag(panel, "VISIBLE"))
        {
            return;
        }

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

            float startX = panelX + localX;
            float startY = panelY + localY;
            var slider = new UiSliderTarget(skillName, vertical, startX, startY, length, range.Min, range.Max, vertical ? size.Height : size.Width);
            double value = skills.GetValueOrDefault(skillName, range.Min);
            float offset = slider.OffsetFor(value);
            targets.Add(new UiHitTarget(
                panel.Name,
                panel.Keyword,
                null,
                vertical ? startX : startX + offset,
                vertical ? startY + offset : startY,
                size.Width,
                size.Height,
                layer + 1,
                vertical ? "VSLIDER" : "HSLIDER",
                Slider: slider));
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
}
