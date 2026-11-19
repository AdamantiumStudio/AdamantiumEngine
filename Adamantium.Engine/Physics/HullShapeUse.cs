using Adamantium.DarkMatter.Shapes;

namespace Adamantium.Engine.Physics;

/// <summary>A hull shape of the game and how many bodies use it.</summary>
internal sealed class HullShapeUse
{
    public HullShapeUse(ConvexHullCollisionShape shape)
    {
        Shape = shape;
    }

    public ConvexHullCollisionShape Shape { get; }

    public int Users { get; set; }
}
