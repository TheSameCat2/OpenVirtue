// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

public sealed class UiHitTesterTests
{
    [Fact]
    public void HitTest_VisiblePanelWithIfKlick_ReturnsPanelAction()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 20;
            ACTION clicked { }
            PANEL menu { PAN_MAP panelMap; POS_X 10; POS_Y 12; IF_KLICK clicked; FLAGS VISIBLE; }
            """);

        UiHitTarget? target = UiHitTester.HitTest(level, 20, 18);

        Assert.NotNull(target);
        Assert.Equal("menu", target.Value.SourceElement);
        Assert.Equal("PANEL", target.Value.ElementKind);
        Assert.Equal("clicked", target.Value.Action);
    }

    [Fact]
    public void HitTest_OverlappingTargets_ReturnsHighestLayer()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 20;
            ACTION low { }
            ACTION high { }
            PANEL lowPanel { PAN_MAP panelMap; LAYER 1; POS_X 0; POS_Y 0; IF_KLICK low; FLAGS VISIBLE; }
            PANEL highPanel { PAN_MAP panelMap; LAYER 3; POS_X 0; POS_Y 0; IF_KLICK high; FLAGS VISIBLE; }
            """);

        UiHitTarget? target = UiHitTester.HitTest(level, 4, 4);

        Assert.NotNull(target);
        Assert.Equal("highPanel", target.Value.SourceElement);
        Assert.Equal("high", target.Value.Action);
    }

    [Fact]
    public void HitTest_HiddenOrActionlessPanel_IsIgnored()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 20;
            ACTION clicked { }
            PANEL hiddenPanel { PAN_MAP panelMap; POS_X 0; POS_Y 0; IF_KLICK clicked; }
            PANEL actionlessPanel { PAN_MAP panelMap; POS_X 0; POS_Y 0; FLAGS VISIBLE; }
            """);

        Assert.Null(UiHitTester.HitTest(level, 4, 4));
    }

    [Fact]
    public void HitTest_UsesDecodedImageSizeWhenDeclarationHasWholeBitmap()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>;
            ACTION clicked { }
            PANEL menu { PAN_MAP panelMap; POS_X 10; POS_Y 10; IF_KLICK clicked; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["panelMap"] = new(30, 12),
        };

        UiHitTarget? inside = UiHitTester.HitTest(level, 39, 21, sizes);
        UiHitTarget? outside = UiHitTester.HitTest(level, 40, 22, sizes);

        Assert.NotNull(inside);
        Assert.Null(outside);
    }

    [Fact]
    public void HitTest_PanelButton_ReturnsButtonPressActionBeforePanelAction()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP panelMap, <panel.pcx>, 0, 0, 80, 40;
            BMAP buttonOn, <button_on.pcx>, 0, 0, 12, 8;
            BMAP buttonOff, <button_off.pcx>, 0, 0, 12, 8;
            ACTION panelClick { }
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { PAN_MAP panelMap; POS_X 10; POS_Y 20; IF_KLICK panelClick; BUTTON 5,6,buttonOn,buttonOff,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);

        UiHitTarget? button = UiHitTester.HitTest(level, 16, 27);
        UiHitTarget? background = UiHitTester.HitTest(level, 12, 22);

        Assert.NotNull(button);
        Assert.Equal("BUTTON", button.Value.TargetKind);
        Assert.Equal("buttonDown", button.Value.Action);
        Assert.Equal("buttonUp", button.Value.ReleaseAction);
        Assert.Equal(new UiButtonIdentity("menu", 0), button.Value.Button);
        Assert.Equal(15, button.Value.X);
        Assert.Equal(26, button.Value.Y);
        Assert.Equal(12, button.Value.Width);
        Assert.Equal(8, button.Value.Height);

        Assert.NotNull(background);
        Assert.Equal("Element", background.Value.TargetKind);
        Assert.Equal("panelClick", background.Value.Action);
    }

    [Fact]
    public void HitTest_PanelButtonWithNullActions_StillReturnsInteractiveButtonState()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <button_on.pcx>, 0, 0, 12, 8;
            PANEL menu { BUTTON 2,3,buttonOn,NULL,NULL,NULL; FLAGS VISIBLE; }
            """);

        UiHitTarget? button = UiHitTester.HitTest(level, 3, 4);

        Assert.NotNull(button);
        Assert.Equal(new UiButtonIdentity("menu", 0), button.Value.Button);
        Assert.Null(button.Value.Action);
        Assert.Null(button.Value.ReleaseAction);
    }

    [Fact]
    public void HitTest_PanelButton_UsesDecodedImageSizeWhenButtonBitmapHasNoRect()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP buttonOn, <button_on.pcx>;
            BMAP buttonOff, <button_off.pcx>;
            ACTION buttonDown { }
            ACTION buttonUp { }
            PANEL menu { POS_X 0; POS_Y 0; BUTTON 2,3,buttonOn,buttonOff,buttonDown,buttonUp; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["buttonOn"] = new(9, 7),
        };

        UiHitTarget? inside = UiHitTester.HitTest(level, 10, 9, sizes);
        UiHitTarget? outside = UiHitTester.HitTest(level, 11, 10, sizes);

        Assert.NotNull(inside);
        Assert.Equal("buttonDown", inside.Value.Action);
        Assert.Null(outside);
    }

    [Fact]
    public void HitTest_HorizontalSlider_UsesLiveSkillPositionAndExposesValueMapping()
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

        UiHitTarget? target = UiHitTester.HitTest(level, 35, 26, skills: skills);

        Assert.NotNull(target);
        Assert.Equal("HSLIDER", target.Value.TargetKind);
        Assert.Equal(30, target.Value.X); // 50% of the 32-pixel travel (40 less the 8-wide knob), OV-20261029-ui-widgets
        Assert.Equal(25, target.Value.Y);
        UiSliderTarget slider = Assert.IsType<UiSliderTarget>(target.Value.Slider);
        Assert.Equal("volume", slider.Skill);
        Assert.False(slider.Vertical);
        Assert.Equal(14, slider.StartX);
        Assert.Equal(25, slider.StartY);
        Assert.Equal(40, slider.Length);
        Assert.Equal(0, slider.Min);
        Assert.Equal(100, slider.Max);
        Assert.Equal(0, slider.ValueAtOffset(-10));
        Assert.Equal(50, slider.ValueAtOffset(16));
        Assert.Equal(100, slider.ValueAtOffset(50));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 9)]
    [InlineData(4.5, 22)]
    [InlineData(6, 36)]
    public void SliderOffset_MeasuredSliders_TravelLengthLessKnobCutToAPixel(double value, float offset)
    {
        // OV-20261029-ui-widgets round 2: a 40-pixel slider, a 4-pixel knob, MIN 2, MAX 6.
        var slider = new UiSliderTarget("ui_s", false, 104, 320, 40, 2, 6, 4);

        Assert.Equal(offset, slider.OffsetFor(value));
    }

    [Fact]
    public void HitTest_VerticalSlider_UsesDecodedThumbSize()
    {
        Level level = Load(
            """
            MAPFILE <test.wmp>;
            BMAP thumb, <thumb.pcx>;
            SKILL gamma { VAL 5; MIN -10; MAX 10; }
            PANEL menu { VSLIDER 2,3,20,thumb,gamma; FLAGS VISIBLE; }
            """);
        var sizes = new Dictionary<string, UiBitmapSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["thumb"] = new(7, 9),
        };

        UiHitTarget? target = UiHitTester.HitTest(level, 3, 19, sizes);

        Assert.NotNull(target);
        Assert.Equal("VSLIDER", target.Value.TargetKind);
        Assert.Equal(2, target.Value.X);
        Assert.Equal(11, target.Value.Y); // 75% of the 11-pixel travel, cut to 8
        Assert.Equal(7, target.Value.Width);
        Assert.Equal(9, target.Value.Height);
        Assert.True(target.Value.Slider!.Value.Vertical);
    }

    [Fact]
    public void HitTest_VisibleCenteredText_UsesLiveGlyphBoundsAndAction()
    {
        Level level = Load(
            "MAPFILE <test.wmp>; STRING label,\"old\"; FONT menuFont,<menu.pcx>,8,10; " +
            "ACTION startGame { } " +
            "TEXT newGame { FONT menuFont; STRING label; POS_X 50; POS_Y 20; " +
            "IF_KLICK startGame; FLAGS VISIBLE,CENTER_X; }");
        var strings = new Dictionary<string, WdlString>(StringComparer.OrdinalIgnoreCase)
        {
            ["label"] = new("PLAY"),
        };

        UiHitTarget? inside = UiHitTester.HitTest(level, 35, 25, strings: strings);
        UiHitTarget? outside = UiHitTester.HitTest(level, 33, 25, strings: strings);

        Assert.NotNull(inside);
        Assert.Equal("newGame", inside.Value.SourceElement);
        Assert.Equal("TEXT", inside.Value.ElementKind);
        Assert.Equal("TEXT", inside.Value.TargetKind);
        Assert.Equal("startGame", inside.Value.Action);
        Assert.Equal(34, inside.Value.X);
        Assert.Equal(32, inside.Value.Width);
        Assert.Null(outside);
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
