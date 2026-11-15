using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A unique identifier of PRC: four unsigned integers, a file structure's or an application's.</summary>
internal sealed class PrcUniqueId : IEquatable<PrcUniqueId>
{
    internal PrcUniqueId(uint first, uint second, uint third, uint fourth)
    {
        First = first;
        Second = second;
        Third = third;
        Fourth = fourth;
    }

    internal uint First { get; }

    internal uint Second { get; }

    internal uint Third { get; }

    internal uint Fourth { get; }

    public bool Equals(PrcUniqueId other) =>
        other != null && First == other.First && Second == other.Second && Third == other.Third && Fourth == other.Fourth;

    public override bool Equals(object other) => Equals(other as PrcUniqueId);

    public override int GetHashCode() => HashCode.Combine(First, Second, Third, Fourth);

    public override string ToString() => $"{First:x8}{Second:x8}{Third:x8}{Fourth:x8}";
}

/// <summary>Where an entity lies on and how it is drawn (PRC GraphicsContent): a layer, a style of the globals and the
/// behaviour bits, each -1 or 0 when the entity has none.</summary>
internal sealed class PrcGraphics
{
    /// <summary>The entity is shown.</summary>
    internal const ushort Show = 0x0001;

    /// <summary>The entity has been removed from the tree.</summary>
    internal const ushort Removed = 0x2000;

    /// <summary>The graphics an entity has before any are read: no layer, no style, every behaviour bit set.</summary>
    internal static PrcGraphics Default { get; } = new() { LayerIndex = -1, StyleIndex = -1, Behavior = 0xFFFF };

    internal int LayerIndex { get; init; }

    internal int StyleIndex { get; init; }

    internal ushort Behavior { get; init; }

    internal bool IsShown => (Behavior & Show) != 0;

    internal bool IsRemoved => (Behavior & Removed) != 0 && Behavior != 0xFFFF;
}

/// <summary>A line style of the globals (PRC_TYPE_GRAPH_Style): a colour or a material, a transparency, a line width.</summary>
internal sealed class PrcStyle
{
    internal string Name { get; init; }

    internal double LineWidth { get; init; }

    internal bool IsMaterial { get; init; }

    /// <summary>The index of the colour, or of the material when <see cref="IsMaterial"/>; -1 for none.</summary>
    internal int ColorOrMaterialIndex { get; init; }

    /// <summary>0 transparent to 255 opaque; null when the style says nothing of it.</summary>
    internal byte? Transparency { get; init; }
}

/// <summary>A material of the globals: a generic material's colour indices and alphas (PRC_TYPE_GRAPH_Material), or a
/// texture application (PRC_TYPE_GRAPH_TextureApplication) that names one.</summary>
internal sealed class PrcMaterial
{
    internal string Name { get; init; }

    internal bool IsTextureApplication { get; init; }

    internal int AmbientIndex { get; init; } = -1;

    internal int DiffuseIndex { get; init; } = -1;

    internal int EmissiveIndex { get; init; } = -1;

    internal int SpecularIndex { get; init; } = -1;

    internal double Shininess { get; init; }

    internal double AmbientAlpha { get; init; } = 1;

    internal double DiffuseAlpha { get; init; } = 1;

    internal double EmissiveAlpha { get; init; } = 1;

    internal double SpecularAlpha { get; init; } = 1;

    /// <summary>For a texture application, the generic material it lays its texture on.</summary>
    internal int MaterialGenericIndex { get; init; } = -1;

    internal int TextureDefinitionIndex { get; init; } = -1;
}

/// <summary>A picture of the globals (PRC_TYPE_GRAPH_Picture), held in one of the file's uncompressed files.</summary>
internal sealed class PrcPicture
{
    internal const int Png = 0;
    internal const int Jpeg = 1;

    internal string Name { get; init; }

    internal int Format { get; init; }

    internal int UncompressedFileIndex { get; init; }

    internal uint Width { get; init; }

    internal uint Height { get; init; }
}

/// <summary>A texture definition of the globals (PRC_TYPE_GRAPH_TextureDefinition): the picture it lays.</summary>
internal sealed class PrcTextureDefinition
{
    internal string Name { get; init; }

    internal int PictureIndex { get; init; }
}

/// <summary>An entity of the tree, the part all of them share: its name, attributes and graphics.</summary>
internal class PrcTreeEntity
{
    internal uint Type { get; init; }

    internal string Name { get; set; }

    internal PrcGraphics Graphics { get; set; } = PrcGraphics.Default;

    internal Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
}

/// <summary>A product occurrence (PRC_TYPE_ASM_ProductOccurence): a node of the assembly, its part, prototype or
/// external data, its sons and where it stands in its father.</summary>
internal sealed class PrcProductOccurrence : PrcTreeEntity
{
    /// <summary>The occurrence is suppressed.</summary>
    internal const byte Suppressed = 0x01;

    internal int PartIndex { get; init; } = -1;

    internal int PrototypeIndex { get; init; } = -1;

    /// <summary>The file structure the prototype lies in; null for this one.</summary>
    internal PrcUniqueId PrototypeFileStructure { get; init; }

    internal int ExternalIndex { get; init; } = -1;

    internal PrcUniqueId ExternalFileStructure { get; init; }

    internal List<int> Sons { get; } = [];

    internal byte Behavior { get; init; }

    internal bool UnitFromCad { get; set; }

    /// <summary>The unit in millimetres.</summary>
    internal double Unit { get; set; }

    /// <summary>Where it stands in its father, as a row-vector matrix; null for none.</summary>
    internal Matrix4x4F? Location { get; set; }

    internal int EntityReferenceCount { get; set; }

    internal PrcMarkupData Markups { get; set; }

    internal int ViewCount { get; set; }

    internal int FilterCount { get; set; }

    internal int SceneDisplayParameterCount { get; set; }
}

/// <summary>A part definition (PRC_TYPE_ASM_PartDefinition): its representation items and its markups.</summary>
internal sealed class PrcPartDefinition : PrcTreeEntity
{
    internal List<PrcRepresentationItem> Items { get; } = [];

    internal PrcMarkupData Markups { get; set; }

    internal int ViewCount { get; set; }
}

/// <summary>A representation item (PRC_TYPE_RI_*): what it shows - its tessellation, its body in the exact geometry,
/// its points - placed by a coordinate system of the globals, and for a set its items.</summary>
internal sealed class PrcRepresentationItem : PrcTreeEntity
{
    internal int CoordinateSystemIndex { get; set; } = -1;

    internal int TessellationIndex { get; set; } = -1;

    /// <summary>The topological context of its body in the exact geometry section; -1 for none.</summary>
    internal int ContextIndex { get; set; } = -1;

    internal int BodyIndex { get; set; } = -1;

    internal bool IsClosed { get; set; }

    /// <summary>A point set's points, a direction's origin and vector.</summary>
    internal List<Vector3<double>> Points { get; } = [];

    /// <summary>A coordinate system's placement as a row-vector matrix.</summary>
    internal Matrix4x4F Transform { get; set; } = Matrix4x4F.Identity;

    internal List<PrcRepresentationItem> Items { get; } = [];
}

/// <summary>The markups of a part or a product (PRC MarkupData): the markups themselves, each with its tessellation,
/// and how many leaders, linked items and annotations go with them.</summary>
internal sealed class PrcMarkupData
{
    internal List<PrcMarkup> Markups { get; } = [];

    internal List<PrcMarkup> Leaders { get; } = [];

    internal int LinkedItemCount { get; set; }

    internal int AnnotationCount { get; set; }
}

/// <summary>A markup or a leader (PRC_TYPE_MKP_Markup, PRC_TYPE_MKP_Leader): its kind and its markup tessellation.</summary>
internal sealed class PrcMarkup : PrcTreeEntity
{
    internal uint MarkupType { get; init; }

    internal uint SubType { get; init; }

    internal int TessellationIndex { get; set; } = -1;
}

/// <summary>A tessellation of the tessellation section; its coordinates are x, y, z triples.</summary>
internal abstract class PrcTessellation
{
    internal uint Type { get; init; }

    internal double[] Coordinates { get; set; } = [];
}

/// <summary>A regular 3D tessellation (PRC_TYPE_TESS_3D): points, normals, the triangulated indices of its faces.</summary>
internal sealed class Prc3DTessellation : PrcTessellation
{
    internal bool HasFaces { get; set; }

    internal bool MustRecalculateNormals { get; set; }

    internal double[] Normals { get; set; } = [];

    internal uint[] WireIndices { get; set; } = [];

    internal uint[] TriangulatedIndices { get; set; } = [];

    internal List<PrcFaceTessellation> Faces { get; } = [];

    internal double[] TextureCoordinates { get; set; } = [];
}

/// <summary>A face of a 3D tessellation (PRC_TYPE_TESS_Face): which kinds of triangulation it has, where they start
/// in the triangulated indices and how many there are, and its graphics.</summary>
internal sealed class PrcFaceTessellation
{
    internal uint[] LineAttributes { get; set; } = [];

    internal uint StartWire { get; set; }

    internal uint[] SizesWire { get; set; } = [];

    internal uint UsedEntities { get; set; }

    internal uint StartTriangulated { get; set; }

    internal uint[] SizesTriangulated { get; set; } = [];

    internal uint TextureCoordinateIndexes { get; set; }

    /// <summary>One RGBA colour a vertex of its triangulation, in order; null for none.</summary>
    internal List<Color> VertexColors { get; set; }
}

/// <summary>A wire tessellation (PRC_TYPE_TESS_3D_Wire): polylines through its points.</summary>
internal sealed class PrcWireTessellation : PrcTessellation
{
    internal uint[] WireIndices { get; set; } = [];
}

/// <summary>A markup tessellation (PRC_TYPE_TESS_Markup): its codes, which say what each run of coordinates draws.</summary>
internal sealed class PrcMarkupTessellation : PrcTessellation
{
    internal uint[] Codes { get; set; } = [];

    internal List<string> Texts { get; } = [];

    internal string Label { get; set; }

    internal byte Behavior { get; set; }
}

/// <summary>A tessellation this reader does not read, kept so the indices of the others stay theirs.</summary>
internal sealed class PrcUnreadTessellation : PrcTessellation
{
}
