using System;

namespace Adamantium.DarkMatter;

/// <summary>Which of the 64 layers collide with which: every one with every other until told, both ways alike.
/// </summary>
internal sealed class LayerCollisions
{
    // One bit for each layer each one collides with.
    private readonly ulong[] _collides = new ulong[PhysicsLayers.Count];

    public LayerCollisions() => Array.Fill(_collides, ulong.MaxValue);

    public bool Collide(int first, int second) => (_collides[first] & (1UL << second)) != 0;

    /// <summary>Makes the two collide or not; whether that changed anything.</summary>
    public bool Set(int first, int second, bool collides)
    {
        if (Collide(first, second) == collides)
            return false;

        SetBit(first, second, collides);
        SetBit(second, first, collides);
        return true;
    }

    private void SetBit(int layer, int other, bool collides)
    {
        var bit = 1UL << other;
        _collides[layer] = collides ? _collides[layer] | bit : _collides[layer] & ~bit;
    }
}
