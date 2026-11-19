using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>A collider's own sizes and placement. While it is fitted to its mesh only <see cref="Scale"/> and
/// <see cref="ChoosesAxis"/> count; the rest stays at its default.</summary>
public sealed record ColliderShapeEdits
{
    /// <summary>Whether the collider fits its mesh rather than keeping the sizes given here.</summary>
    public bool IsFitted { get; set; } = true;

    /// <summary>Where the collider's center stands in its entity's own space.</summary>
    public Vector3F Position { get; set; }

    public QuaternionF Rotation { get; set; } = QuaternionF.Identity;

    /// <summary>How the collider is stretched along its own axes, fitted or not.</summary>
    public Vector3F Scale { get; set; } = Vector3F.One;

    /// <summary>A box's size along its own axes.</summary>
    public Vector3F Size { get; set; }

    /// <summary>A sphere's, capsule's or cylinder's radius.</summary>
    public float Radius { get; set; }

    /// <summary>A capsule's or cylinder's height, end to end.</summary>
    public float Height { get; set; }

    /// <summary>The axis a capsule or cylinder stands along.</summary>
    public ColliderAxis Axis { get; set; } = ColliderAxis.Y;

    /// <summary>Whether a fitted cylinder chooses its axis by the mesh.</summary>
    public bool ChoosesAxis { get; set; } = true;

    /// <summary>Whether it holds a new collider's own values only: fitted to its mesh, unscaled - as no shape at all.
    /// </summary>
    public bool HoldsDefaults() =>
        IsFitted && Position == Vector3F.Zero && Rotation == QuaternionF.Identity && Scale == Vector3F.One
        && Size == Vector3F.Zero && Radius == 0 && Height == 0 && Axis == ColliderAxis.Y && ChoosesAxis;
}
