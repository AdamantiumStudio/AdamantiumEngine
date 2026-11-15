namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A triangle as the traversal builds it: its vertices in traversal order, the normal of each, its face and
/// whether it is reversed.</summary>
internal sealed class PrcDecodedTriangle
{
    internal PrcDecodedTriangle(int first, int second, int third)
    {
        First = first;
        Second = second;
        Third = third;
    }

    internal int First { get; }

    internal int Second { get; }

    internal int Third { get; }

    internal int FirstNormal { get; set; }

    internal int SecondNormal { get; set; }

    internal int ThirdNormal { get; set; }

    internal int Face { get; set; }

    internal bool IsReversed { get; set; }
}
