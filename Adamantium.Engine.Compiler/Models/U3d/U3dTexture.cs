using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A texture resource (ECMA-363 9.8.5).</summary>
internal sealed class U3dTexture
{
    internal string Name { get; init; }

    internal uint Width { get; init; }

    internal uint Height { get; init; }

    internal List<U3dTextureImage> Images { get; } = [];

    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; init; }
}
