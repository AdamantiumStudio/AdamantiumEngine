using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>Shapes standing together as one body: a truck's frame, cab and wheels. Each part collides on its own, with
/// its own contact; the mass is all of theirs.</summary>
public sealed class CompoundCollisionShape : CollisionShape
{
    private readonly CompoundPart[] _parts;
    private readonly float _boundingRadius;

    /// <summary>A compound of <paramref name="parts"/>, none of them a compound itself.</summary>
    public CompoundCollisionShape(IEnumerable<CompoundPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        _parts = [.. parts];
        if (_parts.Length == 0)
            throw new ArgumentException("A compound needs at least one part.", nameof(parts));

        for (var index = 0; index < _parts.Length; index++)
        {
            var part = _parts[index];
            if (part.Shape == null)
                throw new ArgumentException("A part needs a shape.", nameof(parts));

            if (part.Shape is CompoundCollisionShape)
                throw new ArgumentException("A part cannot be a compound itself.", nameof(parts));

            var position = part.Pose.Position;
            var lengthSquared = part.Pose.Rotation.LengthSquared();
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
                || !(lengthSquared > 1e-12f) || !float.IsFinite(lengthSquared))
                throw new ArgumentException("A part needs a finite position and rotation.", nameof(parts));

            _parts[index] = new CompoundPart(part.Shape,
                new Pose(position, QuaternionF.Normalize(part.Pose.Rotation)));
            _boundingRadius = MathF.Max(_boundingRadius, position.Length() + part.Shape.BoundingRadius);
        }

        RequireFiniteReach(nameof(parts));
    }

    /// <summary>The parts, as they stand in the compound's own axes.</summary>
    public IReadOnlyList<CompoundPart> Parts => _parts;

    /// <inheritdoc/>
    public override CollisionShapeKind Kind => CollisionShapeKind.Compound;

    /// <inheritdoc/>
    public override float BoundingRadius => _boundingRadius;

    /// <inheritdoc/>
    public override MassProperties ComputeMass(float density)
    {
        var total = 0.0;
        var weighted = Vector3<double>.Zero;
        for (var index = 0; index < _parts.Length; index++)
        {
            var mass = _parts[index].Shape.ComputeMass(density);
            total += mass.Mass;
            weighted += CenterOf(index, mass) * mass.Mass;
        }

        if (!(total > 0) || !double.IsFinite(total))
            return new MassProperties((float)total, Vector3F.Zero);

        var center = weighted / total;
        var tensor = default(Matrix3x3<double>);
        for (var index = 0; index < _parts.Length; index++)
        {
            var mass = _parts[index].Shape.ComputeMass(density);
            tensor += InertiaMath.ToTensor(mass.Inertia, _parts[index].Pose.Rotation * mass.InertiaRotation, mass.Mass,
                CenterOf(index, mass) - center);
        }

        InertiaMath.Diagonalize(tensor, out var moments, out var axes);
        return new MassProperties((float)total, moments, center.As<float>(), axes);
    }

    /// <inheritdoc/>
    public override BoundingBox<float> ComputeBounds(in Pose pose)
    {
        var bounds = _parts[0].Shape.ComputeBounds(pose.Transform(_parts[0].Pose));
        for (var index = 1; index < _parts.Length; index++)
        {
            var part = _parts[index].Shape.ComputeBounds(pose.Transform(_parts[index].Pose));
            bounds = new BoundingBox<float>(Vector3F.Min(bounds.Minimum, part.Minimum),
                Vector3F.Max(bounds.Maximum, part.Maximum));
        }

        return bounds;
    }

    /// <summary>The point of the hull around the parts farthest along <paramref name="direction"/>.</summary>
    public override Vector3F GetSupport(Vector3F direction)
    {
        var best = Vector3F.Zero;
        var bestDistance = float.NegativeInfinity;
        foreach (var part in _parts)
        {
            var point = part.Pose.TransformPoint(part.Shape.GetSupport(part.Pose.InverseTransformDirection(direction)));
            var distance = Vector3F.Dot(point, direction);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        return best;
    }

    /// <inheritdoc/>
    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        distance = maxDistance;
        normal = default;
        var found = false;
        foreach (var part in _parts)
        {
            var localRay = new Ray<float>(part.Pose.InverseTransformPoint(ray.Position),
                part.Pose.InverseTransformDirection(ray.Direction));
            if (!part.Shape.CastRay(localRay, distance, out var partDistance, out var partNormal))
                continue;

            distance = partDistance;
            normal = part.Pose.TransformDirection(partNormal);
            found = true;
        }

        return found;
    }

    private Vector3<double> CenterOf(int index, in MassProperties mass) =>
        _parts[index].Pose.TransformPoint(mass.Center).As<double>();
}
