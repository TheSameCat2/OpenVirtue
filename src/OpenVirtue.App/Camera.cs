// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The OpenVirtue Authors

using System.Numerics;

namespace OpenVirtue.App;

/// <summary>A simple fly camera: position + yaw/pitch, producing view and projection matrices.</summary>
internal sealed class Camera
{
    public Vector3 Position;
    public float Yaw;   // radians, around world up
    public float Pitch; // radians, clamped to avoid gimbal flip

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Forward));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

    public Matrix4x4 Projection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, aspect, 1f, 200000f);

    public void MoveForward(float amount) => Position += Forward * amount;

    public void MoveRight(float amount) => Position += Right * amount;

    public void MoveUp(float amount) => Position += Vector3.UnitY * amount;

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -1.5f, 1.5f);
    }
}
