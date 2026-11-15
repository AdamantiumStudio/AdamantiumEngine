namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The modifier chain a block was declared in (ECMA-363 9.4.3).</summary>
internal sealed class U3dChain
{
    /// <summary>A node chain.</summary>
    internal const uint NodeChain = 0;

    /// <summary>A model resource chain.</summary>
    internal const uint ModelResourceChain = 1;

    /// <summary>A texture resource chain.</summary>
    internal const uint TextureChain = 2;

    internal U3dChain(string name, uint type)
    {
        Name = name;
        Type = type;
    }

    internal string Name { get; }

    internal uint Type { get; }
}
