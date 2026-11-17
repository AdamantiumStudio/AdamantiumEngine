using System;

namespace Adamantium.Engine.Rendering;

/// <summary>Where a blended draw goes in the frame: the farther from the camera first; of two as far, the one met first,
/// which keeps a two-sided mesh's back before its front. <see cref="Draw"/> is its index among the frame's draws.</summary>
internal readonly record struct BlendedDrawOrder(float DistanceSquared, int Draw) : IComparable<BlendedDrawOrder>
{
    public int CompareTo(BlendedDrawOrder other)
    {
        var byDistance = other.DistanceSquared.CompareTo(DistanceSquared);
        return byDistance != 0 ? byDistance : Draw.CompareTo(other.Draw);
    }
}
