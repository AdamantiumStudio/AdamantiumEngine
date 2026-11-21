namespace Adamantium.DarkMatter;

/// <summary>Any of the 64 layers at once, as one 64-bit number; none for the default.</summary>
public readonly record struct LayerMask
{
    internal LayerMask(ulong bits) => Bits = bits;

    /// <summary>Every layer.</summary>
    public static LayerMask All => new(ulong.MaxValue);

    /// <summary>Only <paramref name="layer"/>.</summary>
    public static LayerMask Of(Layer layer) => new(BitOf(layer));

    /// <summary>One bit for each layer it holds, layer 0 the lowest.</summary>
    internal ulong Bits { get; }

    /// <summary>These layers and <paramref name="layer"/>.</summary>
    public LayerMask With(Layer layer) => new(Bits | BitOf(layer));

    /// <summary>These layers but <paramref name="layer"/>.</summary>
    public LayerMask Without(Layer layer) => new(Bits & ~BitOf(layer));

    /// <summary>Whether it holds <paramref name="layer"/>.</summary>
    public bool Contains(Layer layer) => (Bits & BitOf(layer)) != 0;

    private static ulong BitOf(Layer layer) => 1UL << layer.Index;
}
