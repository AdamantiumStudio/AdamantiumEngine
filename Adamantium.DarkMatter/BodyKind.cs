namespace Adamantium.DarkMatter;

/// <summary>How a body moves.</summary>
public enum BodyKind
{
    /// <summary>Never moves on its own: ground, walls. Only moved by setting its pose.</summary>
    Static,

    /// <summary>Moves at the velocity it is given and pushes dynamic bodies, but nothing pushes it back: platforms,
    /// doors.</summary>
    Kinematic,

    /// <summary>Moved by gravity, forces and contacts.</summary>
    Dynamic
}
