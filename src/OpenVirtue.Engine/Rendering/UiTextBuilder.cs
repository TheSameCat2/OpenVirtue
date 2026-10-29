// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Globalization;

namespace OpenVirtue.Engine.Rendering;

/// <summary>A single screen-space glyph resolved from WDL <c>TEXT</c>/<c>FONT</c> declarations and panel displays.</summary>
public readonly record struct UiTextGlyph(
    string SourceElement,
    string Font,
    int CodePoint,
    float X,
    float Y,
    int Width,
    int Height,
    int Layer,
    int SourceX = 0,
    int SourceY = 0,
    int SourceWidth = 0,
    int SourceHeight = 0,
    int DrawWidth = 0,
    int DrawHeight = 0);

/// <summary>Builds first-pass screen-space glyphs for visible WDL text and panel display declarations.</summary>
public static class UiTextBuilder
{
    /// <summary>Builds visible text/display glyphs in ascending layer order.</summary>
    public static IReadOnlyList<UiTextGlyph> Build(
        Level level,
        IReadOnlyDictionary<string, WdlString>? strings = null,
        IReadOnlyDictionary<string, double>? skills = null,
        IReadOnlyDictionary<string, WdlUiElement>? uiElements = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        IReadOnlyDictionary<string, WdlString> textStrings = strings ?? level.Strings;
        IReadOnlyDictionary<string, double> skillValues = skills ?? level.Skills;
        IReadOnlyDictionary<string, WdlUiElement> elements = uiElements ?? level.UiElements;
        var fonts = GatherFonts(elements);
        var glyphs = new List<UiTextGlyph>();
        foreach (WdlUiElement element in elements.Values)
        {
            if (element.Keyword.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasFlag(element, "VISIBLE") ||
                    FirstPropertyValue(element, "FONT") is not { } fontName ||
                    !fonts.TryGetValue(fontName, out FontDef font))
                {
                    continue;
                }

                AddTextGlyphs(element, fontName, font, textStrings, glyphs);
            }
            else if (element.Keyword.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasFlag(element, "VISIBLE"))
                {
                    continue;
                }

                AddPanelDigits(element, fonts, skillValues, glyphs);
            }
        }

        return glyphs
            .OrderBy(glyph => glyph.Layer)
            .ThenBy(glyph => glyph.SourceElement, StringComparer.OrdinalIgnoreCase)
            .ThenBy(glyph => glyph.Y)
            .ThenBy(glyph => glyph.X)
            .ToArray();
    }

    private static Dictionary<string, FontDef> GatherFonts(IReadOnlyDictionary<string, WdlUiElement> uiElements)
    {
        var fonts = new Dictionary<string, FontDef>(StringComparer.OrdinalIgnoreCase);
        foreach (WdlUiElement element in uiElements.Values)
        {
            if (!element.Keyword.Equals("FONT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int[] numbers = NumberValues(element.Header);
            if (numbers.Length >= 2 && numbers[0] > 0 && numbers[1] > 0)
            {
                fonts[element.Name] = new FontDef(numbers[0], numbers[1]);
            }
        }

        return fonts;
    }

    private static void AddTextGlyphs(
        WdlUiElement element,
        string fontName,
        FontDef font,
        IReadOnlyDictionary<string, WdlString> strings,
        List<UiTextGlyph> glyphs)
    {
        float originX = NumberProperty(element, "POS_X") ?? 0;
        float originY = NumberProperty(element, "POS_Y") ?? 0;
        float sizeY = Math.Max(0, NumberProperty(element, "SIZE_Y") ?? float.PositiveInfinity);
        float offsetY = Math.Max(0, NumberProperty(element, "OFFSET_Y") ?? 0);
        int layer = (int)(NumberProperty(element, "LAYER") ?? 0);
        int advance = TextAdvance(font, element);
        int drawWidth = TextDrawWidth(font, element);
        List<string> lines = TextLines(element, strings).ToList();
        float blockHeight = lines.Count * font.Height;
        float blockTop = HasFlag(element, "CENTER_Y")
            ? originY - (blockHeight / 2)
            : originY;
        float clipTop = blockTop;
        float clipBottom = float.IsPositiveInfinity(sizeY)
            ? float.PositiveInfinity
            : clipTop + sizeY;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            string line = lines[lineIndex];
            float lineWidth = TextLineWidth(line.Length, drawWidth, advance);
            float x = HasFlag(element, "CENTER_X")
                ? originX - (lineWidth / 2)
                : originX;
            float y = blockTop + (lineIndex * font.Height) - offsetY;
            if (!TryClipTextLine(y, font.Height, clipTop, clipBottom, out int sourceY, out int drawHeight, out float drawY))
            {
                continue;
            }

            foreach (char ch in line)
            {
                AddGlyph(
                    glyphs,
                    element.Name,
                    fontName,
                    ch,
                    x,
                    drawY,
                    font,
                    layer,
                    0,
                    sourceY,
                    font.Width,
                    drawHeight,
                    drawWidth,
                    drawHeight);
                x += advance;
            }
        }
    }

    private static IEnumerable<string> TextLines(WdlUiElement element, IReadOnlyDictionary<string, WdlString> strings)
    {
        foreach (string stringName in PropertyValues(element, "STRING"))
        {
            if (stringName.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
                !strings.TryGetValue(stringName, out WdlString value))
            {
                continue;
            }

            var line = new List<char>();
            for (int i = 0; i < value.Value.Length; i++)
            {
                char ch = value.Value[i];
                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n' ||
                    (ch == '\\' && i + 1 < value.Value.Length && value.Value[i + 1] == 'n'))
                {
                    yield return new string(line.ToArray());
                    line.Clear();
                    if (ch == '\\')
                    {
                        i++;
                    }

                    continue;
                }

                line.Add(ch);
            }

            yield return new string(line.ToArray());
        }
    }

    private static void AddPanelDigits(
        WdlUiElement panel,
        IReadOnlyDictionary<string, FontDef> fonts,
        IReadOnlyDictionary<string, double> skills,
        List<UiTextGlyph> glyphs)
    {
        float panelX = NumberProperty(panel, "POS_X") ?? 0;
        float panelY = NumberProperty(panel, "POS_Y") ?? 0;
        int layer = (int)(NumberProperty(panel, "LAYER") ?? 0);
        foreach (WdlUiProperty property in panel.Properties)
        {
            WdlUiValue[] values = ArgumentValues(property.Values);
            if (!property.Keyword.Equals("DIGITS", StringComparison.OrdinalIgnoreCase) ||
                values.Length < 6 ||
                NumberAt(values, 0) is not { } localX ||
                NumberAt(values, 1) is not { } localY ||
                NumberAt(values, 2) is not { } lengthValue ||
                IdentifierAt(values, 3) is not { } fontName ||
                !fonts.TryGetValue(fontName, out FontDef font) ||
                NumberAt(values, 4) is not { } factor ||
                IdentifierAt(values, 5) is not { } skillName)
            {
                continue;
            }

            int length = Math.Max(0, (int)Math.Truncate(lengthValue));
            if (length == 0)
            {
                continue;
            }

            double skill = skills.GetValueOrDefault(skillName);
            string display = FormatDigits(skill, factor, length);
            float x = panelX + localX;
            float y = panelY + localY;
            foreach (char ch in display)
            {
                AddGlyph(glyphs, panel.Name, fontName, ch, x, y, font, layer);
                x += font.Width;
            }
        }
    }

    private static void AddGlyph(
        List<UiTextGlyph> glyphs,
        string sourceElement,
        string fontName,
        int codePoint,
        float x,
        float y,
        FontDef font,
        int layer,
        int sourceX = 0,
        int sourceY = 0,
        int sourceWidth = 0,
        int sourceHeight = 0,
        int drawWidth = 0,
        int drawHeight = 0) =>
        glyphs.Add(new UiTextGlyph(
            sourceElement,
            fontName,
            codePoint,
            x,
            y,
            font.Width,
            font.Height,
            layer,
            sourceX,
            sourceY,
            sourceWidth,
            sourceHeight,
            drawWidth,
            drawHeight));

    private static int TextAdvance(FontDef font, WdlUiElement element)
    {
        if (HasFlag(element, "NARROW"))
        {
            return Math.Max(1, font.Width - 2);
        }

        if (HasFlag(element, "CONDENSED"))
        {
            return Math.Max(1, font.Width - 1);
        }

        return font.Width;
    }

    /// <summary>
    /// A character's drawn width: the whole font cell. <c>CONDENSED</c> and <c>NARROW</c> shorten only
    /// the advance, so each character overlaps the one before (OV-20261029-ui-layout).
    /// </summary>
    private static int TextDrawWidth(FontDef font, WdlUiElement element) => font.Width;

    private static float TextLineWidth(int characterCount, int drawWidth, int advance) =>
        characterCount <= 0 ? 0 : ((characterCount - 1) * advance) + drawWidth;

    private static bool TryClipTextLine(
        float y,
        int height,
        float clipTop,
        float clipBottom,
        out int sourceY,
        out int drawHeight,
        out float drawY)
    {
        float bottom = y + height;
        float clippedTop = Math.Max(y, clipTop);
        float clippedBottom = Math.Min(bottom, clipBottom);
        if (clippedBottom <= clippedTop)
        {
            sourceY = 0;
            drawHeight = 0;
            drawY = y;
            return false;
        }

        sourceY = Math.Max(0, (int)Math.Round(clippedTop - y));
        drawHeight = Math.Max(0, (int)Math.Round(clippedBottom - clippedTop));
        drawY = clippedTop;
        return drawHeight > 0;
    }

    private static string FormatDigits(double skill, float factor, int length)
    {
        // OV-20261029-ui-layout: the value is rounded (3.7 shows 4), right-aligned in len places with
        // spaces before it, and a longer number keeps its last len digits.
        double scaled = skill * factor;
        long rounded = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        bool negative = rounded < 0;
        string digits = Math.Abs(rounded).ToString(CultureInfo.InvariantCulture);
        if (negative)
        {
            digits = "-" + digits;
        }

        if (digits.Length > length)
        {
            return digits[^length..];
        }

        return digits.PadLeft(length, ' ');
    }

    private static bool HasFlag(WdlUiElement element, string flag) =>
        element.Properties.Any(property =>
            property.Keyword.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) &&
            property.Values.Any(value => value.Text.Equals(flag, StringComparison.OrdinalIgnoreCase)));

    private static string? FirstPropertyValue(WdlUiElement element, string name) =>
        PropertyValues(element, name).FirstOrDefault();

    private static IEnumerable<string> PropertyValues(WdlUiElement element, string name)
    {
        foreach (WdlUiProperty property in element.Properties)
        {
            if (!property.Keyword.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (WdlUiValue value in property.Values)
            {
                if (!value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
                {
                    yield return value.Text;
                }
            }
        }
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

    private static WdlUiValue[] ArgumentValues(IReadOnlyList<WdlUiValue> values) =>
        values
            .Where(value => !value.Kind.Equals("Comma", StringComparison.OrdinalIgnoreCase))
            .ToArray();

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

    private static int[] NumberValues(IReadOnlyList<WdlUiValue> values) =>
        values
            .Where(value => value.Kind.Equals("Number", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(value => int.Parse(value.Text, CultureInfo.InvariantCulture))
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

    private readonly record struct FontDef(int Width, int Height);
}
