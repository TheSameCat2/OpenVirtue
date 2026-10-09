// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.Engine.Rendering;

/// <summary>
/// The original renderer's camera projection, as measured black-box in
/// OV-20261009-projection-fov-player-arc, OV-20261009-pixel-aspect-video-modes and
/// OV-20261009-tilt-model-shear-vs-pitch:
/// <list type="bullet">
/// <item>horizontal: a pinhole whose field of view is <c>PLAYER_ARC</c> radians, so the
/// focal length in window pixels is <c>(SCREEN_WIDTH/2) / tan(PLAYER_ARC/2)</c>;</item>
/// <item>vertical: the half-tangent is two thirds of the horizontal one times <c>ASPECT</c>,
/// whatever the window shape, so <c>f_v = 0.75 · SCREEN_HGT / (ASPECT · tan(PLAYER_ARC/2))</c>
/// (no pixel-aspect pre-stretch; every VGA mode looks the same on a 4:3 display);</item>
/// <item>tilt: a y-shear, not a camera pitch. The whole frame moves by
/// <c>-PLAYER_TILT · SCREEN_HGT</c> rows (positive tilt looks down), independent of arc and
/// aspect, and vertical world edges stay vertical.</item>
/// </list>
/// All numbers are in the 3-D window's own pixels (<c>SCREEN_WIDTH</c> × <c>SCREEN_HGT</c>);
/// the host maps that window onto the physical display with <see cref="VirtualDisplayLayout"/>.
/// </summary>
public readonly record struct ProjectionModel
{
    /// <summary>Documented <c>PLAYER_ARC</c> range (manual p.111).</summary>
    public const double MinimumArc = 0.2;
    public const double MaximumArc = 2.0;

    /// <summary>Documented <c>ASPECT</c> range (manual p.102).</summary>
    public const double MinimumAspect = 0.1;
    public const double MaximumAspect = 10.0;

    /// <summary>Vertical half-tangent as a fraction of the horizontal one at <c>ASPECT</c> 1.</summary>
    public const double VerticalTangentRatio = 2.0 / 3.0;

    private ProjectionModel(double playerArc, double aspect, double playerTilt, int screenWidth, int screenHeight)
    {
        PlayerArc = playerArc;
        Aspect = aspect;
        PlayerTilt = playerTilt;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
    }

    public double PlayerArc { get; }

    public double Aspect { get; }

    public double PlayerTilt { get; }

    /// <summary>Width of the 3-D window in virtual pixels (<c>SCREEN_WIDTH</c>).</summary>
    public int ScreenWidth { get; }

    /// <summary>Height of the 3-D window in virtual pixels (<c>SCREEN_HGT</c>).</summary>
    public int ScreenHeight { get; }

    /// <summary>Builds the model from the live skills, clamping to the documented ranges.</summary>
    public static ProjectionModel Create(double playerArc, double aspect, double playerTilt, int screenWidth, int screenHeight)
    {
        if (double.IsNaN(playerArc) || double.IsNaN(aspect) || double.IsNaN(playerTilt))
        {
            throw new ArgumentException("Projection skills must be numbers.");
        }

        return new ProjectionModel(
            Math.Clamp(playerArc, MinimumArc, MaximumArc),
            Math.Clamp(aspect, MinimumAspect, MaximumAspect),
            playerTilt,
            Math.Max(1, screenWidth),
            Math.Max(1, screenHeight));
    }

    /// <summary>The model at the documented defaults (<c>PLAYER_ARC</c> 1, <c>ASPECT</c> 1, no tilt).</summary>
    public static ProjectionModel Default(int screenWidth, int screenHeight) =>
        Create(1, 1, 0, screenWidth, screenHeight);

    /// <summary>Horizontal field of view in radians: exactly <c>PLAYER_ARC</c>.</summary>
    public double HorizontalFieldOfView => PlayerArc;

    /// <summary>tan(PLAYER_ARC / 2): the lateral extent at unit forward distance that reaches the window's side edge.</summary>
    public double HorizontalHalfTangent => Math.Tan(PlayerArc * 0.5);

    /// <summary>The vertical extent at unit forward distance that reaches the window's top or bottom edge (before tilt).</summary>
    public double VerticalHalfTangent => VerticalTangentRatio * Aspect * HorizontalHalfTangent;

    /// <summary>Horizontal focal length in window pixels.</summary>
    public double FocalLengthX => (ScreenWidth * 0.5) / HorizontalHalfTangent;

    /// <summary>Vertical focal length in window pixels.</summary>
    public double FocalLengthY => (ScreenHeight * 0.5) / VerticalHalfTangent;

    /// <summary>Row (from the window top, pixel-edge coordinates) of the eye line: <c>H/2 - PLAYER_TILT · H</c>.</summary>
    public double EyeLineRow => (ScreenHeight * 0.5) - (PlayerTilt * ScreenHeight);

    /// <summary>Column of the window center.</summary>
    public double CenterColumn => ScreenWidth * 0.5;

    /// <summary>
    /// The tilt shear in normalized device units (y up, −1..1 over the window height): the
    /// projected y of every point is offset by this amount.
    /// </summary>
    public double ShearNdc => 2.0 * PlayerTilt;

    /// <summary>Vertical slope (rise per unit of forward distance) of the ray through the window center.</summary>
    public double CenterSlope => -PlayerTilt * ScreenHeight / FocalLengthY;

    /// <summary>
    /// Projects a view-space point (<paramref name="forward"/> &gt; 0 along the line of sight,
    /// <paramref name="right"/> toward the screen's right, <paramref name="up"/> toward its top)
    /// to window pixels measured from the top-left corner.
    /// </summary>
    public (double X, double Y) Project(double forward, double right, double up)
    {
        if (forward <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(forward), "Points must be in front of the eye.");
        }

        return (
            CenterColumn + (FocalLengthX * right / forward),
            EyeLineRow - (FocalLengthY * up / forward));
    }

    /// <summary>
    /// The ray for a <c>SHOOT</c> deviation (<c>SHOOT_X</c>/<c>SHOOT_Y</c>, −1..1 from the window
    /// center to its edges; positive <c>SHOOT_Y</c> is down the screen) as a lateral tangent
    /// (positive = screen right) and a vertical slope, both per unit of forward distance.
    /// </summary>
    public (double LateralTangent, double VerticalSlope) ShootRay(double shootX, double shootY)
    {
        double x = Math.Clamp(shootX, -1, 1);
        double y = Math.Clamp(shootY, -1, 1);
        return (x * HorizontalHalfTangent, (-y * VerticalHalfTangent) + CenterSlope);
    }

    /// <summary>
    /// A right-handed projection matrix for row-vector math (<c>v · M</c>, the
    /// <see cref="Matrix4x4"/> convention) with the camera looking down −Z and depth mapped to
    /// 0..1, i.e. a drop-in for <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/> that carries
    /// the measured horizontal/vertical scales and the tilt shear.
    /// </summary>
    public Matrix4x4 CreateProjectionMatrix(float nearPlane, float farPlane)
    {
        if (nearPlane <= 0 || farPlane <= nearPlane)
        {
            throw new ArgumentOutOfRangeException(nameof(nearPlane), "Need 0 < near < far.");
        }

        var m = new Matrix4x4
        {
            M11 = (float)(1.0 / HorizontalHalfTangent),
            M22 = (float)(1.0 / VerticalHalfTangent),
            M32 = (float)(-ShearNdc),
            M33 = farPlane / (nearPlane - farPlane),
            M34 = -1f,
            M43 = nearPlane * farPlane / (nearPlane - farPlane),
        };
        return m;
    }
}
