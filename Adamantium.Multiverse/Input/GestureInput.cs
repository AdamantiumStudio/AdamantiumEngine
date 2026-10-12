using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input;

/// <summary>A step of a gesture of fingers on the output, as the host recognized it: how far the fingers moved, how
/// much they spread and how far they turned since the last step. A part the host did not recognize is left at rest:
/// no move, a scale of 1, no turn.</summary>
public record struct GestureInput
{
    public GestureInput()
    {
    }

    /// <summary>How far the fingers moved together, in the output's units.</summary>
    public Vector2F Translation;

    /// <summary>How much the fingers spread: above 1 apart, below 1 together.</summary>
    public float Scale = 1;

    /// <summary>How far the fingers turned, in degrees, clockwise on screen.</summary>
    public float Rotation;
}
