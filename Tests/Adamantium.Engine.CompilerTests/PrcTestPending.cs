namespace Adamantium.Engine.CompilerTests;

/// <summary>A node waiting to be given its code: the code so far, the root bit highest, and its length.</summary>
internal sealed class PrcTestPending
{
    public PrcTestPending(PrcTestNode node, long code, int length)
    {
        Node = node;
        Code = code;
        Length = length;
    }

    public PrcTestNode Node { get; }

    public long Code { get; }

    public int Length { get; }
}
