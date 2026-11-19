using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The hull shapes of a game's bodies, one for each hull and carry they share, kept while a body uses it.
/// </summary>
internal sealed class HullShapeCache
{
    private readonly Dictionary<HullShapeKey, HullShapeUse> _uses = new();

    /// <summary>The shape of <paramref name="hull"/> carried by <paramref name="carry"/>, made on first use; its key goes
    /// in <paramref name="takenBy"/>, to give back with <see cref="GiveBack"/>.</summary>
    public ConvexHullCollisionShape Take(ConvexHull hull, in Matrix4x4F carry, float convexRadius,
        List<HullShapeKey> takenBy)
    {
        var key = new HullShapeKey(hull, KeepSixDigits(carry), convexRadius);
        if (!_uses.TryGetValue(key, out var use))
        {
            // A hull carried is still a hull: carried as it is, not hulled anew from its corners.
            use = new HullShapeUse(new ConvexHullCollisionShape(hull.Transform(carry), convexRadius));
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

    // A carry read back from a turned entity's matrix differs in its last digits: rounded for the key, copies share the
    // shape the first of them made.
    private static Matrix4x4F KeepSixDigits(Matrix4x4F carry)
    {
        for (var index = 0; index < 16; index++)
        {
            carry[index] = KeepSixDigits(carry[index]);
        }

        return carry;
    }

    private static float KeepSixDigits(float value)
    {
        if (value == 0 || !float.IsFinite(value))
            return value;

        var unit = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - 5);
        return (float)(Math.Round(value / unit) * unit);
    }
}
