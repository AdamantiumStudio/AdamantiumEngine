using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>One file structure of a PRC file (PRC_TYPE_ASM_FileStructure): an assembly or a part as one CAD file had it,
/// with its own schema, globals, tree and tessellations. Its exact geometry is not read.</summary>
internal sealed class PrcFileStructure
{
    internal int Index { get; init; }

    internal PrcUniqueId Id { get; init; }

    internal uint MinimalVersionForRead { get; set; }

    internal uint AuthoringVersion { get; set; }

    internal PrcSchema Schema { get; set; } = PrcSchema.Empty;

    /// <summary>The private files the file structure's header embeds - the PNG or JPEG data its pictures name.</summary>
    internal List<byte[]> UncompressedFiles { get; } = [];

    internal List<PrcUniqueId> ReferencedFileStructures { get; } = [];

    /// <summary>The fonts of markup text, each key of each font in turn, as a markup's font entity numbers them.</summary>
    internal List<PrcFont> Fonts { get; } = [];

    internal List<Vector3<double>> Colors { get; } = [];

    internal List<PrcPicture> Pictures { get; } = [];

    internal List<PrcTextureDefinition> TextureDefinitions { get; } = [];

    internal List<PrcMaterial> Materials { get; } = [];

    internal List<PrcStyle> Styles { get; } = [];

    internal List<PrcRepresentationItem> CoordinateSystems { get; } = [];

    internal List<PrcPartDefinition> Parts { get; } = [];

    internal List<PrcProductOccurrence> Occurrences { get; } = [];

    /// <summary>The index of the root product occurrence among <see cref="Occurrences"/>.</summary>
    internal int RootIndex { get; set; } = -1;

    /// <summary>The tessellation section's entities in order, a highly compressed one rebuilt as a regular 3D
    /// tessellation; one this reader does not read is a <see cref="PrcUnreadTessellation"/>.</summary>
    internal List<PrcTessellation> Tessellations { get; } = [];

    /// <summary>How many tessellations the section says it holds.</summary>
    internal int DeclaredTessellationCount { get; set; }

    /// <summary>How many topological contexts the exact geometry section holds.</summary>
    internal int TopologicalContextCount { get; set; }
}
