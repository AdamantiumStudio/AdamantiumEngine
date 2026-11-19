using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The hull shapes of a game's bodies, one for each hull and scale they share, kept while a body uses it.
/// </summary>
internal sealed class HullShapeCache
{
    private readonly Dictionary<HullShapeKey, HullShapeUse> _uses = new();

    /// <summary>The shape of <paramref name="hull"/> at <paramref name="scale"/>, made on first use; its key goes in
    /// <paramref name="takenBy"/>, to give back with <see cref="GiveBack"/>.</summary>
    public ConvexHullCollisionShape Take(ConvexHull hull, Vector3F scale, Vector3F roundedScale,
        List<HullShapeKey> takenBy)
    {
        var key = new HullShapeKey(hull, roundedScale);
        if (!_uses.TryGetValue(key, out var use))
        {
            // A hull scaled is still a hull: carried as it is, not hulled anew from its corners.
            use = new HullShapeUse(new ConvexHullCollisionShape(hull.Transform(Matrix4x4.CreateScale<float>(scale))));
            _uses[key] = use;
        }

        use.Users++;
        takenBy.Add(key);
        return use.Shape;
    }

    /// <summary>Gives back the shapes <paramref name="taken"/> names; one no body uses any more is let go.</summary>
    public void GiveBack(List<HullShapeKey> taken)
    {
        foreach (var key in taken)
        {
            if (_uses.TryGetValue(key, out var use) && --use.Users == 0)
                _uses.Remove(key);
        }

        taken.Clear();
    }

    public void Clear() => _uses.Clear();
}
