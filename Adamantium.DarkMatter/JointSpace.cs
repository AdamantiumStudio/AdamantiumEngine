namespace Adamantium.DarkMatter;

/// <summary>Where the points and the axis of <see cref="JointSettings"/> are given.</summary>
public enum JointSpace
{
    /// <summary>In the world, where the bodies stand when the joint is asked for.</summary>
    World,

    /// <summary>In each body's own axes, from its origin: right however far the bodies move before the joint is taken.
    /// </summary>
    Bodies
}
