using System;

namespace Adamantium.DarkMatter;

/// <summary>One group of a grouped search: its key, and its bodies, a slice of the caller's buffer.</summary>
public readonly ref struct BodyGroup<TKey>
{
    internal BodyGroup(TKey key, ReadOnlySpan<BodyId> bodies)
    {
        Key = key;
        Bodies = bodies;
    }

    public TKey Key { get; }
    public ReadOnlySpan<BodyId> Bodies { get; }
    public int Count => Bodies.Length;
}
