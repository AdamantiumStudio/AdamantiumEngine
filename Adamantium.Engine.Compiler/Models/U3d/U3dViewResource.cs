namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A view resource (ECMA-363 9.8.2): what of its passes the import looks at.</summary>
internal sealed class U3dViewResource
{
    internal string Name { get; init; }

    internal uint PassCount { get; init; }

    /// <summary>The node the first pass renders, the world when empty.</summary>
    internal string RootNodeName { get; init; } = string.Empty;

    internal bool FogEnabled { get; init; }
}
