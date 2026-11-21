using System;

namespace Adamantium.DarkMatter;

/// <summary>The ready conditions of a search, looked at before any <c>Where</c>: each told narrows it, but the area
/// and the body passed by, one of each, the last told.</summary>
internal readonly struct FindConditions
{
    // Each kind kept, as a bit of its value; none for every kind.
    public int Kinds { get; init; }
    public bool AwakeOnly { get; init; }
    public bool AsleepOnly { get; init; }

    // The layers passed by, so conditions left as they are keep them all.
    public ulong PassedLayers { get; init; }
    public BodyId Except { get; init; }
    public bool HasLeastMass { get; init; }
    public float LeastMass { get; init; }
    private readonly BoundingBox<double> _box;
    private readonly BoundingSphere<double> _sphere;

    // One area at a time: a box or a sphere.
    public bool HasBox { get; init; }
    public BoundingBox<double> Box { get => _box; init => _box = value; }
    public bool HasSphere { get; init; }
    public BoundingSphere<double> Sphere { get => _sphere; init => _sphere = value; }

    public FindConditions WithKind(BodyKind kind) => this with { Kinds = Kinds | 1 << (int)kind };

    public bool Matches(in BodyView body)
    {
        if (Kinds != 0 && (Kinds & 1 << (int)body.Kind) == 0)
            return false;

        if (AwakeOnly && body.IsSleeping || AsleepOnly && !body.IsSleeping)
            return false;

        if ((PassedLayers & 1UL << body.Layer.Index) != 0 || body.Id == Except)
            return false;

        return !HasLeastMass || body.Mass > LeastMass;
    }

    /// <summary>Whether a center of mass is within the area told, or there is none: looked at first, by the center
    /// alone, as the search walks every body through here.</summary>
    public bool IsWithin(WorldPosition center)
    {
        if (!HasBox && !HasSphere)
            return true;

        double x = center.X, y = center.Y, z = center.Z;
        if (HasBox)
        {
            ref readonly var box = ref _box;
            return x >= box.Minimum.X && y >= box.Minimum.Y && z >= box.Minimum.Z && x <= box.Maximum.X
                   && y <= box.Maximum.Y && z <= box.Maximum.Z;
        }

        ref readonly var sphere = ref _sphere;
        double dx = x - sphere.Center.X, dy = y - sphere.Center.Y, dz = z - sphere.Center.Z;
        return dx * dx + dy * dy + dz * dz <= sphere.Radius * sphere.Radius;
    }

    public static void RequireArea(in BoundingBox<double> box, string parameterName)
    {
        if (!IsFinite(box.Minimum) || !IsFinite(box.Maximum))
            throw new ArgumentOutOfRangeException(parameterName, "An area must be finite.");
    }

    public static void RequireArea(in BoundingSphere<double> sphere, string parameterName)
    {
        if (!IsFinite(sphere.Center) || !double.IsFinite(sphere.Radius) || sphere.Radius < 0)
            throw new ArgumentOutOfRangeException(parameterName, "An area must be finite, its radius not below zero.");
    }

    private static bool IsFinite(Vector3<double> vector) =>
        double.IsFinite(vector.X) && double.IsFinite(vector.Y) && double.IsFinite(vector.Z);
}
