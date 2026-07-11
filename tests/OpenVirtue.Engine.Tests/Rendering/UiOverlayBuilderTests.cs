// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public sealed class UiOverlayBuilderTests
{
    [Fact]
    public void Build_VisibleOverlayWithBitmap_ReturnsScreenSprite()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP titleMap, <title.pcx>, 4, 5, 64, 32;
            OVERLAY titleOverlay { BMAP titleMap; LAYER 2; POS_X 12; POS_Y 24; FLAGS VISIBLE, ABSPOS; }
            OVERLAY hiddenOverlay { BMAP titleMap; POS_X 1; FLAGS ABSPOS; }
            """);

        UiOverlaySprite overlay = Assert.Single(UiOverlayBuilder.Build(level));

        Assert.Equal("titleOverlay", overlay.SourceElement);
        Assert.Equal("titleMap", overlay.Bitmap);
        Assert.Equal(12, overlay.X);
        Assert.Equal(24, overlay.Y);
        Assert.Equal(64, overlay.Width);
        Assert.Equal(32, overlay.Height);
        Assert.Equal(2, overlay.Layer);
        Assert.True(overlay.Absolute);
    }

    [Fact]
    public void Build_MouseSprite_RequiresMouseModeAndUsesOverlayPositionAsHotspot()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            MSPRITE cursor;
            BMAP cursorMap, <cursor.pcx>, 0, 0, 16, 24;
            OVERLAY cursor { BMAP cursorMap; LAYER 7; POS_X 3; POS_Y 5; FLAGS VISIBLE; }
            """);
        var skills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["MOUSE_MODE"] = 0,
            ["MOUSE_X"] = 20,
            ["MOUSE_Y"] = 30,
        };

        Assert.Empty(UiOverlayBuilder.Build(level, skills: skills));

        skills["MOUSE_MODE"] = 2;
        UiOverlaySprite cursor = Assert.Single(UiOverlayBuilder.Build(level, skills: skills));

        Assert.Equal("cursor", cursor.SourceElement);
        Assert.Equal(17, cursor.X);
        Assert.Equal(25, cursor.Y);
        Assert.Equal(7, cursor.Layer);
        Assert.True(cursor.Absolute);
        Assert.Contains(level.UiElements.Values, element =>
            element.Keyword.Equals("MSPRITE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_OverlayWithOvlyResource_ResolvesBitmapLikeResource()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            OVLY cursorMap, <cursor.pcx>, 0, 0, 16, 24;
            OVERLAY cursor { OVLYS cursorMap; POS_X -3; POS_Y 7; FLAGS VISIBLE; }
            """);

        UiOverlaySprite overlay = Assert.Single(UiOverlayBuilder.Build(level));

        Assert.Equal("cursorMap", overlay.Bitmap);
        Assert.Equal(-3, overlay.X);
        Assert.Equal(7, overlay.Y);
        Assert.Equal(16, overlay.Width);
        Assert.Equal(24, overlay.Height);
        Assert.False(overlay.Absolute);
    }

    [Fact]
    public void Build_VisiblePanelWithBackground_ReturnsPanelBackground()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 12;
            PANEL statusPanel { PAN_MAP panelMap; LAYER 1; POS_X 5; POS_Y 6; FLAGS VISIBLE; }
            """);

        UiOverlaySprite panel = Assert.Single(UiOverlayBuilder.Build(level));

        Assert.Equal("statusPanel", panel.SourceElement);
        Assert.Equal("panelMap", panel.Bitmap);
        Assert.Equal(5, panel.X);
        Assert.Equal(6, panel.Y);
        Assert.Equal(80, panel.Width);
        Assert.Equal(12, panel.Height);
        Assert.True(panel.Absolute);
    }

    [Fact]
    public void BuildPanelWindows_VisiblePanelWindow_ReturnsRuntimeSourceCutout()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP scrollMap, <scroll.pcx>;
            SKILL scrollX { VAL 0; }
            SKILL scrollY { VAL 0; }
            PANEL menu { POS_X 10; POS_Y 20; WINDOW 2,3,30,12,scrollMap,scrollX,scrollY; FLAGS VISIBLE; }
            """);
        var skills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["scrollX"] = 5,
            ["scrollY"] = 7,
        };

        UiOverlaySprite window = Assert.Single(UiOverlayBuilder.BuildPanelWindows(level, skills));

        Assert.Equal("menu", window.SourceElement);
        Assert.Equal("scrollMap", window.Bitmap);
        Assert.Equal(12, window.X);
        Assert.Equal(23, window.Y);
        Assert.Equal(30, window.Width);
        Assert.Equal(12, window.Height);
        Assert.Equal(5, window.SourceX);
        Assert.Equal(7, window.SourceY);
        Assert.Equal(30, window.SourceWidth);
        Assert.Equal(12, window.SourceHeight);
    }

    [Fact]
    public void BuildPanelWindows_ClampsSourceToDecodedBitmapSize()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP scrollMap, <scroll.pcx>;
            SKILL scrollX { VAL 0; }
            SKILL scrollY { VAL 0; }
            PANEL menu { WINDOW 0,0,30,12,scrollMap,scrollX,scrollY; FLAGS VISIBLE; }
            """);
        var skills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["scrollX"] = 999,
            ["scrollY"] = 999,
        };
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["scrollMap"] = new(80, 20),
        };

        UiOverlaySprite window = Assert.Single(UiOverlayBuilder.BuildPanelWindows(level, skills, sizes));

        Assert.Equal(50, window.SourceX);
        Assert.Equal(8, window.SourceY);
    }

    [Fact]
    public void BuildPanelWindows_HiddenPanel_IsSkipped()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP scrollMap, <scroll.pcx>;
            SKILL scrollX { VAL 0; }
            SKILL scrollY { VAL 0; }
            PANEL menu { WINDOW 0,0,30,12,scrollMap,scrollX,scrollY; }
            """);

        Assert.Empty(UiOverlayBuilder.BuildPanelWindows(level, level.Skills));
    }

    [Fact]
    public void BuildPanelWidgets_ButtonUsesInactiveBitmap()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <on.pcx>, 0, 0, 12, 8;
            BMAP buttonOff, <off.pcx>, 0, 0, 14, 9;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { POS_X 10; POS_Y 20; BUTTON 3,4,buttonOn,buttonOff,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);

        UiOverlaySprite button = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills));

        Assert.Equal("menu", button.SourceElement);
        Assert.Equal("buttonOff", button.Bitmap);
        Assert.Equal(13, button.X);
        Assert.Equal(24, button.Y);
        Assert.Equal(14, button.Width);
        Assert.Equal(9, button.Height);
    }

    [Fact]
    public void BuildPanelWidgets_PressedButtonUsesActiveBitmap()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <on.pcx>, 0, 0, 12, 8;
            BMAP buttonOff, <off.pcx>, 0, 0, 14, 9;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { POS_X 10; POS_Y 20; BUTTON 3,4,buttonOn,buttonOff,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);

        UiOverlaySprite button = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(
            level,
            level.Skills,
            pressedButton: new UiButtonIdentity("menu", 0)));

        Assert.Equal("buttonOn", button.Bitmap);
        Assert.Equal(12, button.Width);
        Assert.Equal(8, button.Height);
    }

    [Fact]
    public void BuildPanelWidgets_ButtonWithNullInactiveBitmap_IsSkipped()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <on.pcx>, 0, 0, 12, 8;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { BUTTON 0,0,buttonOn,NULL,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);

        Assert.Empty(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills));
    }

    [Fact]
    public void BuildPanelWidgets_PressedButtonWithNullInactiveBitmap_BecomesVisible()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <on.pcx>, 0, 0, 12, 8;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { BUTTON 0,0,buttonOn,NULL,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);

        UiOverlaySprite button = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(
            level,
            level.Skills,
            pressedButton: new UiButtonIdentity("menu", 0)));

        Assert.Equal("buttonOn", button.Bitmap);
    }

    [Fact]
    public void BuildPanelWidgets_ButtonUsesDecodedImageSizeWhenInactiveBitmapHasNoRect()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <on.pcx>;
            BMAP buttonOff, <off.pcx>;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { BUTTON 0,0,buttonOn,buttonOff,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["buttonOff"] = new(22, 11),
        };

        UiOverlaySprite button = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills, sizes));

        Assert.Equal("buttonOff", button.Bitmap);
        Assert.Equal(22, button.Width);
        Assert.Equal(11, button.Height);
    }

    [Fact]
    public void BuildPanelWidgets_HorizontalSlider_PositionsThumbFromLiveSkillRange()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP thumb, <thumb.pcx>, 0, 0, 8, 6;
            SKILL volume { VAL 25; MIN 0; MAX 100; }
            PANEL menu { POS_X 10; POS_Y 20; HSLIDER 4,5,40,thumb,volume; FLAGS VISIBLE; }
            """);
        var skills = new Dictionary<string, double>(level.Skills, StringComparer.OrdinalIgnoreCase)
        {
            ["volume"] = 50,
        };

        UiOverlaySprite slider = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, skills));

        Assert.Equal("thumb", slider.Bitmap);
        Assert.Equal(34, slider.X);
        Assert.Equal(25, slider.Y);
        Assert.Equal(8, slider.Width);
        Assert.Equal(6, slider.Height);
    }

    [Fact]
    public void BuildPanelWidgets_VerticalSlider_PositionsThumbAndUsesDecodedSize()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP thumb, <thumb.pcx>;
            SKILL gamma { VAL 5; MIN -10; MAX 10; }
            PANEL menu { POS_X 10; POS_Y 20; VSLIDER 2,3,20,thumb,gamma; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["thumb"] = new(7, 9),
        };

        UiOverlaySprite slider = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills, sizes));

        Assert.Equal(12, slider.X);
        Assert.Equal(38, slider.Y);
        Assert.Equal(7, slider.Width);
        Assert.Equal(9, slider.Height);
    }

    [Fact]
    public void BuildPanelWidgets_VerticalBar_ShiftsBitmapSourceFromLiveSkill()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP meter, <meter.pcx>, 0, 0, 10, 80;
            SKILL health { VAL 3; }
            PANEL hud { POS_X 4; POS_Y 5; VBAR 2,3,20,meter,2,health; FLAGS VISIBLE; }
            """);

        UiOverlaySprite bar = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills));

        Assert.Equal("meter", bar.Bitmap);
        Assert.Equal(6, bar.X);
        Assert.Equal(8, bar.Y);
        Assert.Equal(10, bar.Width);
        Assert.Equal(20, bar.Height);
        Assert.Equal(0, bar.SourceX);
        Assert.Equal(6, bar.SourceY);
        Assert.Equal(10, bar.SourceWidth);
        Assert.Equal(20, bar.SourceHeight);
    }

    [Fact]
    public void BuildPanelWidgets_HorizontalBar_ClampsSourceToDecodedBitmapSize()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP meter, <meter.pcx>;
            SKILL charge { VAL 99; }
            PANEL hud { HBAR 2,3,30,meter,2,charge; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["meter"] = new(80, 9),
        };

        UiOverlaySprite bar = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills, sizes));

        Assert.Equal(2, bar.X);
        Assert.Equal(3, bar.Y);
        Assert.Equal(30, bar.Width);
        Assert.Equal(9, bar.Height);
        Assert.Equal(50, bar.SourceX);
        Assert.Equal(0, bar.SourceY);
        Assert.Equal(30, bar.SourceWidth);
        Assert.Equal(9, bar.SourceHeight);
    }

    [Fact]
    public void BuildPanelWidgets_PictureUsesSkillSelectedTextureFrame()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP iconOne, <one.pcx>, 0, 0, 11, 12;
            BMAP iconTwo, <two.pcx>, 0, 0, 21, 22;
            TEXTURE iconTex { BMAPS iconOne, iconTwo; }
            SKILL iconIndex { VAL 2; }
            PANEL menu { POS_X 10; POS_Y 20; PICTURE 3,4,iconTex,iconIndex; FLAGS VISIBLE; }
            """);

        UiOverlaySprite picture = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills));

        Assert.Equal("menu", picture.SourceElement);
        Assert.Equal("iconTwo", picture.Bitmap);
        Assert.Equal(13, picture.X);
        Assert.Equal(24, picture.Y);
        Assert.Equal(21, picture.Width);
        Assert.Equal(22, picture.Height);
    }

    [Fact]
    public void BuildPanelWidgets_PictureClampsOutOfRangeSkillToLastFrame()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP iconOne, <one.pcx>, 0, 0, 11, 12;
            BMAP iconTwo, <two.pcx>, 0, 0, 21, 22;
            TEXTURE iconTex { BMAPS iconOne, iconTwo; }
            SKILL iconIndex { VAL 9; }
            PANEL menu { PICTURE 0,0,iconTex,iconIndex; FLAGS VISIBLE; }
            """);

        UiOverlaySprite picture = Assert.Single(UiOverlayBuilder.BuildPanelWidgets(level, level.Skills));

        Assert.Equal("iconTwo", picture.Bitmap);
    }

    private static Level Load(string main)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["test.wmp"] = "VERTEX 0 0 0;#0\nVERTEX 1 0 0;#1\nREGION room 0 10;#0\nPLAYER_START 0 0 0 0;#1",
        };

        return LevelLoader.LoadCore("test.wdl", main, n => resources.GetValueOrDefault(Path.GetFileName(n)));
    }
}
