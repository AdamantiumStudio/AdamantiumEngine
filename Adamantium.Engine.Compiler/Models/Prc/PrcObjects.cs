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

    /// <summary>For a texture application, the texture application of the next level of texture; -1 for none.</summary>
    internal int NextTextureIndex { get; init; } = -1;

    /// <summary>For a texture application, which of a vertex's sets of texture coordinates it is read with.</summary>
    internal int TextureCoordinateSet { get; init; }
}

/// <summary>A font key of the globals' markup fonts (FontKeysSameFont): the font's family name, a size and its
/// attributes - bold, italic, underlined and the rest.</summary>
internal sealed class PrcFont
{
    internal string Name { get; init; }

    internal int Size { get; init; }

    internal byte Attributes { get; init; }
}

/// <summary>A picture of the globals (PRC_TYPE_GRAPH_Picture), held in one of the file's uncompressed files: PNG,
/// JPEG, or flate-compressed pixels of one to four components.</summary>
internal sealed class PrcPicture
{
    internal const int Png = 0;
    internal const int Jpeg = 1;
    internal const int RgbBytes = 2;
    internal const int RgbaBytes = 3;
    internal const int GreyBytes = 4;
    internal const int GreyAlphaBytes = 5;

    internal string Name { get; init; }

    internal int Format { get; init; }

    internal int UncompressedFileIndex { get; init; }

    internal uint Width { get; init; }

    internal uint Height { get; init; }
}

/// <summary>A texture definition of the globals (PRC_TYPE_GRAPH_TextureDefinition): the picture it lays, how its
/// texture coordinates are found, what of the picture it applies and how, how it wraps, and how its coordinates are
/// transformed. Enumerated values are those of the specification, from 0 for unknown.</summary>
internal sealed class PrcTextureDefinition
{
    /// <summary>Coordinates stored on the 3D tessellation.</summary>
    internal const int MappingStored = 1;

    /// <summary>Coordinates worked out by a mapping operator.</summary>
    internal const int MappingOperator = 3;

    internal const int FunctionModulate = 1;
    internal const int FunctionReplace = 2;
    internal const int FunctionBlend = 3;
    internal const int FunctionDecal = 4;

    internal const int WrapUnknown = 0;
    internal const int WrapRepeat = 1;
    internal const int WrapClampToBorder = 2;
    internal const int WrapClamp = 3;
    internal const int WrapClampToEdge = 4;
    internal const int WrapMirroredRepeat = 5;

    /// <summary>The texture's alpha is tested against <see cref="AlphaTestReference"/>.</summary>
    internal const int ApplyingAlphaTest = 0x2;

    /// <summary>The red, green, blue and alpha components of the mapping attributes.</summary>
    internal const uint ComponentsRgba = 0x000F;

    internal string Name { get; init; }

    internal int PictureIndex { get; init; }

    internal int Dimension { get; init; }

    internal int MappingType { get; init; }

    internal int MappingOperatorKind { get; init; }

    /// <summary>Which components of the picture are applied (PRC_TEXTURE_MAPPING_*).</summary>
    internal uint MappingAttributes { get; init; }

    internal int Function { get; init; }

    internal byte ApplyingMode { get; init; }

    internal double AlphaTestReference { get; init; }

    internal int WrapS { get; init; }

    internal int WrapT { get; init; }

    /// <summary>How the texture coordinates are transformed; null for not at all.</summary>
    internal PrcTextureTransformation Transformation { get; init; }
}

/// <summary>A texture transformation (PRC_TYPE_GRAPH_TextureTransformation): a 2D transformation of the texture
/// coordinates, which s and t it inverts first.</summary>
internal sealed class PrcTextureTransformation
{
    internal bool FlipS { get; init; }

    internal bool FlipT { get; init; }

    /// <summary>The 2D transformation in a row-vector matrix: s and t axes in its first two rows, the translation in its
    /// last, the homogeneous part in its last column; (s, t, 0, 1) times it.</summary>
    internal Matrix4x4<double> Matrix { get; init; } = Matrix4x4<double>.Identity;
}

/// <summary>An entity of the tree, the part all of them share: its name, attributes, graphics and the identifier other
/// entities refer to it by.</summary>
internal class PrcTreeEntity
{
    internal uint Type { get; init; }

    internal string Name { get; set; }

    internal PrcGraphics Graphics { get; set; } = PrcGraphics.Default;

    /// <summary>The entity's PRC unique identifier in its file structure; 0 for none.</summary>
    internal uint UniqueId { get; set; }

    internal Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
}

/// <summary>What a reference points at (ReferenceData): the type of the target, the file structure it lies in, and
/// its unique identifier there; or, for a reference on topology, a face of a body.</summary>
internal sealed class PrcReferenceTarget
{
    internal uint Type { get; init; }

    /// <summary>The file structure the target lies in; null for the referring entity's own.</summary>
    internal PrcUniqueId FileStructure { get; init; }

    internal uint UniqueId { get; init; }

    internal bool IsTopology { get; init; }
}

/// <summary>A reference to an entity with what it redefines of it (PRC_TYPE_MISC_EntityReference): its own graphics -
/// the style and visibility it gives the target - and a coordinate system of the globals it places the target by.</summary>
internal sealed class PrcEntityReference : PrcTreeEntity
{
    /// <summary>A reference coordinate system of the globals; -1 for none.</summary>
    internal int CoordinateSystemIndex { get; init; } = -1;

    /// <summary>The entity referred to; null for none.</summary>
    internal PrcReferenceTarget Target { get; init; }
}

/// <summary>A filter of entities (PRC_TYPE_ASM_Filter): which layers and which entities it keeps or takes away, and
/// whether it is the active one.</summary>
internal sealed class PrcFilter : PrcTreeEntity
{
    internal bool IsActive { get; init; }

    /// <summary>Whether the layers are the ones kept rather than the ones taken away.</summary>
    internal bool LayersInclusive { get; init; }

    internal List<uint> Layers { get; } = [];

    /// <summary>Whether the entities are the ones kept rather than the ones taken away.</summary>
    internal bool EntitiesInclusive { get; init; }

    internal List<PrcEntityReference> Entities { get; } = [];
}

/// <summary>A light of a scene (PRC_TYPE_GRAPH_*Light): its colours as indices of the globals' colour components, and
/// for a point, directional or spot light where it is, where it shines and how it fades.</summary>
internal sealed class PrcLight
{
    internal uint Type { get; init; }

    internal string Name { get; init; }

    internal int AmbientIndex { get; init; } = -1;

    internal int DiffuseIndex { get; init; } = -1;

    internal int SpecularIndex { get; init; } = -1;

    internal Vector3<double> Location { get; init; }

    internal Vector3<double> Direction { get; init; }

    internal double ConstantAttenuation { get; init; } = 1;

    internal double LinearAttenuation { get; init; }

    internal double QuadraticAttenuation { get; init; }

    internal double Intensity { get; init; } = 1;

    /// <summary>A spot light's half angle in degrees.</summary>
    internal double FallOffAngle { get; init; }

    internal double FallOffExponent { get; init; }
}

/// <summary>A camera (PRC_TYPE_GRAPH_Camera): where it is, what it looks at, which way is up, its field of view in
/// radians - or for an orthographic one its scale - its aspect, clipping distances and zoom.</summary>
internal sealed class PrcCamera
{
    internal string Name { get; init; }

    internal bool IsOrthographic { get; init; }

    internal Vector3<double> Position { get; init; }

    internal Vector3<double> LookAt { get; init; }

    internal Vector3<double> Up { get; init; }

    internal double FieldOfViewX { get; init; }

    internal double FieldOfViewY { get; init; }

    internal double Aspect { get; init; }

    internal double Near { get; init; }

    internal double Far { get; init; }

    internal double Zoom { get; init; }
}

/// <summary>Scene display parameters (PRC_TYPE_GRAPH_SceneDisplayParameters): the lights and camera of a scene,
/// whether it is the active one, its centre of rotation, clipping planes and default styles, and whether what it
/// places is placed absolutely rather than in its sub-assembly.</summary>
internal sealed class PrcSceneDisplayParameters
{
    internal string Name { get; init; }

    internal bool IsActive { get; init; }

    internal List<PrcLight> Lights { get; } = [];

    /// <summary>Null for none.</summary>
    internal PrcCamera Camera { get; set; }

    /// <summary>Null for none.</summary>
    internal Vector3<double>? RotationCenter { get; set; }

    internal int ClippingPlaneCount { get; set; }

    internal int BackgroundStyleIndex { get; set; } = -1;

    internal bool IsAbsolute { get; set; }
}

/// <summary>A view (PRC_TYPE_MKP_View): annotations shown together on a plane, with the scene they are seen in, the
/// items they link and the filters they show the model by.</summary>
internal sealed class PrcView : PrcTreeEntity
{
    internal List<PrcReferenceTarget> Annotations { get; } = [];

    /// <summary>Null for none.</summary>
    internal PrcSceneDisplayParameters Scene { get; set; }

    internal bool IsAnnotationView { get; set; }

    internal bool IsDefault { get; set; }

    internal List<PrcReferenceTarget> LinkedItems { get; } = [];

    internal List<PrcFilter> Filters { get; } = [];
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

    /// <summary>Entities under it it redefines - their place, style or visibility.</summary>
    internal List<PrcEntityReference> EntityReferences { get; } = [];

    internal PrcMarkupData Markups { get; set; }

    internal List<PrcView> Views { get; } = [];

    /// <summary>The filter applied to what its prototype loads; null for none.</summary>
    internal PrcFilter EntityFilter { get; set; }

    /// <summary>The filters it can be shown by, at most one of them active.</summary>
    internal List<PrcFilter> DisplayFilters { get; } = [];

    internal List<PrcSceneDisplayParameters> SceneDisplayParameters { get; } = [];
}

/// <summary>A part definition (PRC_TYPE_ASM_PartDefinition): its representation items and its markups.</summary>
internal sealed class PrcPartDefinition : PrcTreeEntity
{
    internal List<PrcRepresentationItem> Items { get; } = [];

    internal PrcMarkupData Markups { get; set; }

    internal List<PrcView> Views { get; } = [];
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

    /// <summary>When normals are recalculated, the angle between two triangles, in degrees, up to which they share
    /// their normals.</summary>
    internal double CreaseAngle { get; set; }

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
