namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The entity type codes of the PRC Format Specification 8137 ("Base types for serialization"): each entity in
/// a section starts with its type as an UnsignedInteger.</summary>
internal static class PrcTypes
{
    /// <summary>A null entity.</summary>
    internal const uint Root = 0;

    internal const uint PrcBase = 1;
    internal const uint PrcBaseWithGraphics = 2;
    internal const uint PrcBaseNoReference = 3;

    internal const uint Curve = 10;
    internal const uint Surface = 75;
    internal const uint SurfacePlane = Surface + 11;
    internal const uint Topology = 140;
    internal const uint TopologyContext = Topology + 1;

    internal const uint Tessellation = 170;
    internal const uint TessellationBase = Tessellation + 1;
    internal const uint Tessellation3D = Tessellation + 2;
    internal const uint Tessellation3DCompressed = Tessellation + 3;
    internal const uint TessellationFace = Tessellation + 4;
    internal const uint Tessellation3DWire = Tessellation + 5;
    internal const uint TessellationMarkup = Tessellation + 6;

    internal const uint Miscellaneous = 200;
    internal const uint Attribute = Miscellaneous + 1;
    internal const uint CartesianTransformation = Miscellaneous + 2;
    internal const uint EntityReference = Miscellaneous + 3;
    internal const uint MarkupLinkedItem = Miscellaneous + 4;
    internal const uint ReferenceOnPrcBase = Miscellaneous + 5;
    internal const uint ReferenceOnTopology = Miscellaneous + 6;
    internal const uint GeneralTransformation = Miscellaneous + 7;

    internal const uint RepresentationItem = 230;
    internal const uint RepresentationItemContent = RepresentationItem + 1;
    internal const uint BrepModel = RepresentationItem + 2;
    internal const uint RepresentationCurve = RepresentationItem + 3;
    internal const uint Direction = RepresentationItem + 4;
    internal const uint Plane = RepresentationItem + 5;
    internal const uint PointSet = RepresentationItem + 6;
    internal const uint PolyBrepModel = RepresentationItem + 7;
    internal const uint PolyWire = RepresentationItem + 8;
    internal const uint Set = RepresentationItem + 9;
    internal const uint CoordinateSystem = RepresentationItem + 10;

    internal const uint Assembly = 300;
    internal const uint ModelFile = Assembly + 1;
    internal const uint FileStructure = Assembly + 2;
    internal const uint FileStructureGlobals = Assembly + 3;
    internal const uint FileStructureTree = Assembly + 4;
    internal const uint FileStructureTessellation = Assembly + 5;
    internal const uint FileStructureGeometry = Assembly + 6;
    internal const uint FileStructureExtraGeometry = Assembly + 7;
    internal const uint ProductOccurrence = Assembly + 10;
    internal const uint PartDefinition = Assembly + 11;
    internal const uint Filter = Assembly + 20;

    internal const uint Markup = 500;
    internal const uint View = Markup + 1;
    internal const uint MarkupItem = Markup + 2;
    internal const uint Leader = Markup + 3;
    internal const uint AnnotationItem = Markup + 4;
    internal const uint AnnotationSet = Markup + 5;
    internal const uint AnnotationReference = Markup + 6;

    internal const uint Graphics = 700;
    internal const uint Style = Graphics + 1;
    internal const uint Material = Graphics + 2;
    internal const uint Picture = Graphics + 3;
    internal const uint TextureApplication = Graphics + 11;
    internal const uint TextureDefinition = Graphics + 12;
    internal const uint TextureTransformation = Graphics + 13;
    internal const uint LinePattern = Graphics + 21;
    internal const uint FillPattern = Graphics + 22;
    internal const uint DottingPattern = Graphics + 23;
    internal const uint HatchingPattern = Graphics + 24;
    internal const uint SolidPattern = Graphics + 25;
    internal const uint VPicturePattern = Graphics + 26;
    internal const uint AmbientLight = Graphics + 31;
    internal const uint PointLight = Graphics + 32;
    internal const uint DirectionalLight = Graphics + 33;
    internal const uint SpotLight = Graphics + 34;
    internal const uint SceneDisplayParameters = Graphics + 41;
    internal const uint Camera = Graphics + 42;

    /// <summary>Whether the entities of <paramref name="type"/> can be referenced, and so carry the CAD and PRC
    /// identifiers at the end of their base content.</summary>
    internal static bool IsReferenceable(uint type) => type switch
    {
        ProductOccurrence or PartDefinition or Filter => true,
        >= BrepModel and <= CoordinateSystem => true,
        >= View and <= AnnotationReference => true,
        Style or Material or TextureApplication or TextureDefinition or LinePattern => true,
        >= DottingPattern and <= VPicturePattern => true,
        >= AmbientLight and <= SpotLight => true,
        SceneDisplayParameters or Camera => true,
        _ => false
    };
}
