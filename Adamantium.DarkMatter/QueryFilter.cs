namespace Adamantium.DarkMatter;

/// <summary>What a query of a <see cref="PhysicsSimulation"/> passes by.</summary>
public readonly struct QueryFilter
{
    // Kept as the layers passed by, so a filter left as it is meets them all.
    private readonly ulong _passedLayers;

    /// <summary>A body passed by: the car a wheel is cast from, say; none for none.</summary>
    public BodyId Except { get; init; }

    /// <summary>The layers whose bodies it meets; all unless told.</summary>
    public LayerMask Layers
    {
        get => new(~_passedLayers);
        init => _passedLayers = ~value.Bits;
    }

    /// <summary>Whether it meets triggers too; they are passed by unless told.</summary>
    public bool MeetsTriggers { get; init; }
}
