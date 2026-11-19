using Adamantium.DarkMatter.Shapes;

namespace Adamantium.Engine.Physics;

/// <summary>A shape of the game made from a hull or a mesh, and how many bodies use it.</summary>
internal sealed class CarriedShapeUse
{
    public CarriedShapeUse(CollisionShape shape)
    {
        Shape = shape;
    }

    public CollisionShape Shape { get; }

    public int Users { get; set; }
}
