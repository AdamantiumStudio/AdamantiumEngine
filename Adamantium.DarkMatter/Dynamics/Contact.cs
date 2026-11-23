using System.Collections.Generic;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Two parts of two bodies whose boxes in the broad phase overlap, kept from step to step so the impulses of
/// the last step start the next one. Each body links its contacts in a list of its own.</summary>
internal sealed class Contact
{
    public ulong Key;
    public int First;
    public int Second;
    public int FirstPart;
    public int SecondPart;
    public int ListIndex;
    public float StaticFriction;
    public float DynamicFriction;
    public float RollingResistance;
    public float Restitution;

    /// <summary>The pair of a part and a surface this contact with it belongs to, one for each way the surface faces the
    /// part; null for a contact of two convex parts or for the pair itself.</summary>
    public Contact Holder;

    /// <summary>The contacts of a part and a surface, one for each way the triangles touched face, kept by their pair.
    /// </summary>
    public List<Contact> Pieces;

    /// <summary>Where this contact stands in its holder's <see cref="Pieces"/>.</summary>
    public int PieceIndex;

    /// <summary>Whether triangles facing this way were met in this step.</summary>
    public bool IsSeen;

    /// <summary>The normal of a contact with a surface in the surface's own axes, which a turning surface keeps.</summary>
    public Vector3F SurfaceNormal;

    public Vector3F Normal;
    public Vector3F Tangent;
    public Vector3F Bitangent;
    public int PointCount;
    public readonly ContactPointState[] Points;

    /// <summary>The radius the pair rolls on - its moving round part's, zero for a pair that does not roll - and the
    /// axis it rolls about, zero for any way across the normal.</summary>
    public float RollingLever;
    public Vector3F RollingAxis;

    /// <summary>Whether the touch slid faster than the solver holds when it was last solved: it rubs by the dynamic
    /// friction until it slows down again. A new touch is held.</summary>
    public bool IsSliding;

    /// <summary>The turn the pair resists, kept from step to step; the axes it is resisted about - the rolling axis
    /// alone, or the tangents - and the masses of the pair's turn about each.</summary>
    public Vector3F RollingImpulse;
    public Vector3F RollingFirstAxis;
    public Vector3F RollingSecondAxis;
    public float RollingFirstMass;
    public float RollingSecondMass;

    /// <summary>Whether one of the parts is a trigger: met only where they overlap, and kept in the world's lists of
    /// triggers, apart from the pairs that push.</summary>
    public bool IsTrigger;

    /// <summary>Whether the pair was last told as touching: its touch told begun, and not yet ended.</summary>
    public bool IsToldTouching;

    public Contact NextOfFirst;
    public Contact PreviousOfFirst;
    public Contact NextOfSecond;
    public Contact PreviousOfSecond;

    public Contact(int pointCapacity)
    {
        Points = new ContactPointState[pointCapacity];
    }

    public bool IsTouching => PointCount > 0;

    /// <summary>The key of the pair of part slots, the lesser first.</summary>
    public static ulong MakeKey(int first, int second) =>
        first < second ? ((ulong)(uint)first << 32) | (uint)second : ((ulong)(uint)second << 32) | (uint)first;

    /// <summary>The body of the pair that is not <paramref name="body"/>.</summary>
    public int GetOther(int body) => body == First ? Second : First;

    /// <summary>The next contact in <paramref name="body"/>'s list.</summary>
    public Contact GetNext(int body) => body == First ? NextOfFirst : NextOfSecond;

    public Contact GetPrevious(int body) => body == First ? PreviousOfFirst : PreviousOfSecond;

    public void SetNext(int body, Contact next)
    {
        if (body == First)
            NextOfFirst = next;
        else
            NextOfSecond = next;
    }

    public void SetPrevious(int body, Contact previous)
    {
        if (body == First)
            PreviousOfFirst = previous;
        else
            PreviousOfSecond = previous;
    }
}
