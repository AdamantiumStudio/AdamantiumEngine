using System;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The extension a New Object Type block declared (ECMA-363 9.4.5), which its blocks are told by.</summary>
internal sealed class U3dExtension
{
    internal string Name { get; init; }

    internal string Vendor { get; init; }

    internal Guid Id { get; init; }
}
