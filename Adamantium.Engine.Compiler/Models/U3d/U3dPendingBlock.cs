namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A block waiting its turn, with the modifier chain it came in; null for one at the top of the file.</summary>
internal sealed class U3dPendingBlock
{
    internal U3dPendingBlock(U3dBlock block, U3dChain chain)
    {
        Block = block;
        Chain = chain;
    }

    internal U3dBlock Block { get; }

    internal U3dChain Chain { get; }
}
