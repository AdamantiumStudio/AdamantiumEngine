using System;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A unique identifier of PRC: four unsigned integers, a file structure's or an application's.</summary>
internal sealed class PrcUniqueId : IEquatable<PrcUniqueId>
{
    internal PrcUniqueId(uint first, uint second, uint third, uint fourth)
    {
        First = first;
        Second = second;
        Third = third;
        Fourth = fourth;
    }

    internal uint First { get; }

    internal uint Second { get; }

    internal uint Third { get; }

    internal uint Fourth { get; }

    public bool Equals(PrcUniqueId other) =>
        other != null && First == other.First && Second == other.Second && Third == other.Third && Fourth == other.Fourth;

    public override bool Equals(object other) => Equals(other as PrcUniqueId);

    public override int GetHashCode() => HashCode.Combine(First, Second, Third, Fourth);

    public override string ToString() => $"{First:x8}{Second:x8}{Third:x8}{Fourth:x8}";
}
