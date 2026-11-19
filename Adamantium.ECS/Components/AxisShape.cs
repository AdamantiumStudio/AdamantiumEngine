using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>Sizes of a collider that stands along one of its axes - a capsule, a cylinder.</summary>
internal static class AxisShape
{
    /// <summary>The unit vector of <paramref name="axis"/>.</summary>
    public static Vector3F Direction(ColliderAxis axis) =>
        axis == ColliderAxis.X ? Vector3F.UnitX : axis == ColliderAxis.Y ? Vector3F.UnitY : Vector3F.UnitZ;

    /// <summary>How long <paramref name="size"/> is along <paramref name="axis"/>.</summary>
    public static float Along(Vector3F size, ColliderAxis axis) =>
        axis == ColliderAxis.X ? size.X : axis == ColliderAxis.Y ? size.Y : size.Z;

    /// <summary>The wider of <paramref name="size"/>'s two sides across <paramref name="axis"/>.</summary>
    public static float Across(Vector3F size, ColliderAxis axis) =>
        axis == ColliderAxis.X ? Math.Max(size.Y, size.Z)
        : axis == ColliderAxis.Y ? Math.Max(size.X, size.Z)
        : Math.Max(size.X, size.Y);

    /// <summary>Stands a shape made along Y about the origin on the segment from <paramref name="start"/> to
    /// <paramref name="end"/>, its middle between them.</summary>
    public static Matrix4x4<double> StandUp(Vector3F start, Vector3F end)
    {
        var axis = end - start;
        var length = axis.Length();
        var direction = length > 0 ? axis / length : Vector3F.UnitY;
        var cosine = Vector3F.Dot(Vector3F.UnitY, direction);
        var turnAxis = Vector3F.Cross(Vector3F.UnitY, direction);
        var sine = turnAxis.Length();
        var turn = sine < 1e-6f
            ? cosine > 0 ? QuaternionF.Identity : Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI)
            : Quaternion.CreateFromAxisAngle(turnAxis / sine, MathF.Atan2(sine, cosine));
        return Matrix4x4.CreateFromQuaternion<double>(turn.As<double>()) *
               Matrix4x4.CreateTranslation<double>(((start + end) * 0.5f).As<double>());
    }
}
