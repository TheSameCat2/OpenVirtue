// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;
using OpenVirtue.Engine.Rendering;

namespace OpenVirtue.Engine.Tests.Rendering;

/// <summary>
/// Encodes the three projection probes: OV-20261009-projection-fov-player-arc (horizontal
/// FOV = PLAYER_ARC radians), OV-20261009-pixel-aspect-video-modes (vertical scale
/// 0.75·H/(ASPECT·tan(arc/2))) and OV-20261009-tilt-model-shear-vs-pitch (y-shear of
/// −PLAYER_TILT·H rows). Expected numbers are the closed-form values the notes adopted; the
/// measured fits agree with them within 0.1–1 %.
/// </summary>
public sealed class ProjectionModelTests
{
    [Theory]
    [InlineData(0.5, 1253.2)]
    [InlineData(1.0, 585.8)]
    [InlineData(1.5, 343.5)]
    [InlineData(2.0, 205.5)]
    public void FocalLengthX_Width640_IsHalfWidthOverTangentOfHalfArc(double arc, double expected)
    {
        ProjectionModel model = ProjectionModel.Create(arc, 1, 0, 640, 480);

        Assert.Equal(expected, model.FocalLengthX, 0.1);
    }

    [Fact]
    public void FocalLengthX_Width320_FollowsWindowWidthOnly()
    {
        Assert.Equal(292.9, ProjectionModel.Create(1, 1, 0, 320, 200).FocalLengthX, 0.1);
        Assert.Equal(292.9, ProjectionModel.Create(1, 1, 0, 320, 400).FocalLengthX, 0.1);
    }

    [Fact]
    public void HorizontalFieldOfView_DefaultArc_IsOneRadian()
    {
        ProjectionModel model = ProjectionModel.Default(640, 480);

        Assert.Equal(1.0, model.HorizontalFieldOfView, 1e-12);
        Assert.Equal(57.3, model.HorizontalFieldOfView * 180 / Math.PI, 0.05);
    }

    [Theory]
    [InlineData(640, 480, 659.0)]
    [InlineData(320, 200, 274.6)]
    [InlineData(320, 240, 329.5)]
    [InlineData(320, 400, 549.1)]
    public void FocalLengthY_VideoModes_IsThreeQuartersHeightOverTangent(int width, int height, double expected)
    {
        ProjectionModel model = ProjectionModel.Create(1, 1, 0, width, height);

        Assert.Equal(expected, model.FocalLengthY, 0.1);
    }

    [Theory]
    [InlineData(0.5, 1318.0)]
    [InlineData(2.0, 329.5)]
    public void FocalLengthY_AspectSkill_DividesVerticalScale(double aspect, double expected)
    {
        ProjectionModel model = ProjectionModel.Create(1, aspect, 0, 640, 480);

        Assert.Equal(expected, model.FocalLengthY, 0.1);
        Assert.Equal(585.8, model.FocalLengthX, 0.1); // horizontal scale untouched
    }

    [Fact]
    public void Project_SixteenStepSquareFaceAt48_Is195By220PixelsAt640x480()
    {
        // The pixel-aspect probe's yellow face: 16 wide, 16 high, 48 steps ahead, eye 8 above its floor.
        ProjectionModel model = ProjectionModel.Default(640, 480);

        (double left, double top) = model.Project(48, -8, 8);
        (double right, double bottom) = model.Project(48, 8, -8);

        Assert.Equal(195.3, right - left, 0.2);
        Assert.Equal(219.7, bottom - top, 0.2);
        Assert.Equal(1.125, (bottom - top) / (right - left), 0.002);
        Assert.Equal(240, (top + bottom) / 2, 1e-9); // centered on the eye line
    }

    [Fact]
    public void Project_SquareFaceAt320x200_IsShorterThanWideInFramebufferPixels()
    {
        ProjectionModel model = ProjectionModel.Default(320, 200);

        (double left, double top) = model.Project(48, -8, 8);
        (double right, double bottom) = model.Project(48, 8, -8);

        Assert.Equal(0.9375, (bottom - top) / (right - left), 0.002);
    }

    [Theory]
    [InlineData(0.0, 240)]
    [InlineData(0.5, 0)]
    [InlineData(-0.5, 480)]
    [InlineData(1.0, -240)]
    public void EyeLineRow_Tilt_MovesByTiltTimesWindowHeight(double tilt, double expectedRow)
    {
        Assert.Equal(expectedRow, ProjectionModel.Create(1, 1, tilt, 640, 480).EyeLineRow, 1e-9);
    }

    [Fact]
    public void EyeLineRow_Tilt_IsIndependentOfArcAndAspect()
    {
        // OV-20261009-tilt-model-shear-vs-pitch: the shift is a screen-space quantity.
        Assert.Equal(480, ProjectionModel.Create(2, 1, -0.5, 640, 480).EyeLineRow, 1e-9);
        Assert.Equal(480, ProjectionModel.Create(1, 2, -0.5, 640, 480).EyeLineRow, 1e-9);
    }

    [Fact]
    public void Project_StepTopUnderNegativeTilt_MatchesMeasuredRows()
    {
        // Tilt room: step top 32 above the eye at 48 steps; measured rows 41 / 326 / 261.
        Assert.Equal(40.7, ProjectionModel.Create(1, 1, -0.5, 640, 480).Project(48, 0, 32).Y, 0.5);
        Assert.Equal(325.9, ProjectionModel.Create(2, 1, -0.5, 640, 480).Project(48, 0, 32).Y, 0.5);
        Assert.Equal(260.3, ProjectionModel.Create(1, 2, -0.5, 640, 480).Project(48, 0, 32).Y, 0.5);
    }

    [Fact]
    public void Project_VerticalEdgeUnderTilt_StaysVertical()
    {
        ProjectionModel model = ProjectionModel.Create(1, 1, 0.5, 640, 480);

        (double bottomX, _) = model.Project(48, 6, -8);
        (double topX, _) = model.Project(48, 6, 32);

        Assert.Equal(bottomX, topX, 1e-9);
    }

    [Fact]
    public void ShootRay_Center_FollowsTiltAndEdgesFollowHalfTangents()
    {
        ProjectionModel model = ProjectionModel.Create(1, 1, 0.5, 640, 480);

        (double lateral, double slope) = model.ShootRay(0, 0);
        Assert.Equal(0, lateral, 1e-12);
        Assert.Equal(-0.5 * 480 / model.FocalLengthY, slope, 1e-12); // positive tilt looks down

        (double edgeLateral, _) = model.ShootRay(1, 0);
        Assert.Equal(Math.Tan(0.5), edgeLateral, 1e-12);

        // OV-20261009-shoot-mapping: positive SHOOT_Y aims UP (SHOOT_Y +0.25 hit 4 steps above
        // the eye at 44 steps; −0.25 the same amount below).
        (_, double upSlope) = ProjectionModel.Default(640, 480).ShootRay(0, 1);
        Assert.Equal((2.0 / 3.0) * Math.Tan(0.5), upSlope, 1e-12);
        (_, double quarterUp) = ProjectionModel.Default(640, 480).ShootRay(0, 0.25);
        Assert.Equal(4.0, quarterUp * 44, 0.05);
    }

    [Fact]
    public void CreateProjectionMatrix_TiltedModel_AgreesWithProject()
    {
        ProjectionModel model = ProjectionModel.Create(1.5, 0.8, 0.25, 640, 480);
        Matrix4x4 matrix = model.CreateProjectionMatrix(1, 200000);

        // view space: x right, y up, camera looks down -z (System.Numerics / CreateLookAt convention)
        var view = new Vector4(5f, 3f, -48f, 1f);
        Vector4 clip = Vector4.Transform(view, matrix);
        double ndcX = clip.X / clip.W;
        double ndcY = clip.Y / clip.W;
        double pixelX = (ndcX + 1) * 0.5 * 640;
        double pixelY = (1 - ndcY) * 0.5 * 480;

        (double expectedX, double expectedY) = model.Project(48, 5, 3);
        Assert.Equal(expectedX, pixelX, 1e-3);
        Assert.Equal(expectedY, pixelY, 1e-3);
        Assert.InRange(clip.Z / clip.W, 0, 1);
    }

    [Fact]
    public void CreateProjectionMatrix_NoTilt_MatchesPerspectiveFieldOfViewScales()
    {
        ProjectionModel model = ProjectionModel.Default(640, 480);
        Matrix4x4 matrix = model.CreateProjectionMatrix(1, 200000);

        Assert.Equal(1 / Math.Tan(0.5), matrix.M11, 1e-5);
        Assert.Equal(1 / ((2.0 / 3.0) * Math.Tan(0.5)), matrix.M22, 1e-5);
        Assert.Equal(0, matrix.M32, 1e-9);
    }

    [Fact]
    public void CreateProjectionMatrix_WithLookAtView_ScreenRightIsCrossOfForwardAndUp()
    {
        // The host builds its view with Matrix4x4.CreateLookAt (right-handed): a world point
        // displaced along cross(forward, up) must land on the screen's right, where
        // Project() puts a positive 'right' argument. (cross(up, forward) is the mirror image.)
        ProjectionModel model = ProjectionModel.Create(1, 1, 0.25, 640, 480);
        var position = new Vector3(3, 8, -5);
        Vector3 forward = Vector3.Normalize(new Vector3(0.6f, 0, 0.8f));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(position, position + forward, Vector3.UnitY)
            * model.CreateProjectionMatrix(1, 200000);

        Vector3 world = position + (forward * 48) + (right * 8) + (Vector3.UnitY * 4);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
        double pixelX = (clip.X / clip.W + 1) * 0.5 * 640;
        double pixelY = (1 - clip.Y / clip.W) * 0.5 * 480;

        (double expectedX, double expectedY) = model.Project(48, 8, 4);
        Assert.True(pixelX > 320, $"expected the point on the right half, got column {pixelX}");
        Assert.Equal(expectedX, pixelX, 1e-2);
        Assert.Equal(expectedY, pixelY, 1e-2);
    }

    [Fact]
    public void Create_OutOfRangeSkills_ClampsToDocumentedRanges()
    {
        ProjectionModel model = ProjectionModel.Create(5, 0.01, 0, 640, 480);

        Assert.Equal(ProjectionModel.MaximumArc, model.PlayerArc);
        Assert.Equal(ProjectionModel.MinimumAspect, model.Aspect);
    }
}
