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
    public float Friction;
    public float Restitution;

    public Vector3F Normal;
    public Vector3F Tangent;
    public Vector3F Bitangent;
    public int PointCount;
    public readonly ContactPointState[] Points;

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
