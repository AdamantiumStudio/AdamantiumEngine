using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A vertex of a run of triangulated data: its point, normal and texture indices and its color.</summary>
internal readonly struct PrcRunVertex
{
    internal PrcRunVertex(long point, long normal, long texture, Color color)
    {
        Point = point;
        Normal = normal;
        Texture = texture;
        Color = color;
    }

    internal long Point { get; }

    internal long Normal { get; }

    internal long Texture { get; }

    internal Color Color { get; }
}
