using System;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>A joint where its bodies stand now: its point on each from its center of mass, how far the points stand
/// apart, and the axes, all in the world.</summary>
internal readonly struct JointFrame
{
    public JointFrame(Joint joint, in BodyState first, in BodyState second)
    {
        ArmOfFirst = Vector3F.Transform(joint.PointOnFirst - first.CenterOfMass, first.Rotation);
        ArmOfSecond = Vector3F.Transform(joint.PointOnSecond - second.CenterOfMass, second.Rotation);
        // The centers' gap in double, made float once small: the bodies stand side by side wherever they are. The world
        // holds a joint from its anchor.
        var firstPosition = joint.First >= 0 ? first.Position : joint.WorldAnchor;
        Apart = (second.Position - firstPosition).As<float>() + ArmOfSecond - ArmOfFirst;
        AxisOfFirst = Vector3F.Transform(joint.AxisOnFirst, first.Rotation);
        AxisOfSecond = Vector3F.Transform(joint.AxisOnSecond, second.Rotation);
        ReferenceOfFirst = Vector3F.Transform(joint.ReferenceOnFirst, first.Rotation);
    }

    public Vector3F ArmOfFirst { get; }

    public Vector3F ArmOfSecond { get; }

    /// <summary>From the first body's point to the second's.</summary>
    public Vector3F Apart { get; }

    public Vector3F AxisOfFirst { get; }

    public Vector3F AxisOfSecond { get; }

    /// <summary>A direction square to the first body's axis, turning with it.</summary>
    public Vector3F ReferenceOfFirst { get; }

    /// <summary>How far the second body has turned about the first's axis since the joint was added, in radians.
    /// </summary>
    public float MeasureAngle(Joint joint, in BodyState second)
    {
        var turned = Vector3F.Transform(joint.ReferenceOnSecond, second.Rotation);
        return MathF.Atan2(Vector3F.Dot(Vector3F.Cross(ReferenceOfFirst, turned), AxisOfFirst),
            Vector3F.Dot(ReferenceOfFirst, turned));
    }
}
