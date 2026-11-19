namespace Adamantium.DarkMatter;

/// <summary>How a joint holds two bodies together.</summary>
public enum JointKind
{
    /// <summary>Welded: the bodies move as one.</summary>
    Fixed,

    /// <summary>A ball in a socket: the point stays shared, the bodies turn freely about it.</summary>
    Ball,

    /// <summary>A hinge: the point stays shared, the bodies turn about the axis alone - a door, a wheel.</summary>
    Hinge,

    /// <summary>A slider: the bodies keep their turn and slide along the axis alone - a drawer, a piston.</summary>
    Slider,

    /// <summary>A rod or a spring between two points: their distance kept, held between limits, or pulled toward its
    /// length by a spring.</summary>
    Distance
}
