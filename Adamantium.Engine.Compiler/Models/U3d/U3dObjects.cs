using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The kinds of node of ECMA-363 9.5.</summary>
internal enum U3dNodeKind
{
    Group,
    Model,
    Light,
    View
}

/// <summary>The kinds of geometry a model resource generates (ECMA-363 9.6).</summary>
internal enum U3dGeometryKind
{
    Mesh,
    PointSet,
    LineSet
}

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

/// <summary>A parent of a node and where the node stands relative to it, as a row-vector matrix: the file's column
/// major matrix of column vectors read element by element.</summary>
internal sealed class U3dParent
{
    internal string Name { get; init; }

    internal Matrix4x4F Transform { get; init; }
}

/// <summary>A node of the node palette (ECMA-363 9.5).</summary>
internal sealed class U3dNode
{
    internal U3dNodeKind Kind { get; init; }

    internal string Name { get; init; }

    internal List<U3dParent> Parents { get; } = [];

    /// <summary>The model, light or view resource; null for a group.</summary>
    internal string ResourceName { get; set; }

    /// <summary>A model node's visibility: 0 none, 1 front, 2 back, 3 both.</summary>
    internal uint Visibility { get; set; } = 1;

    /// <summary>A view node's own fields; null for other nodes.</summary>
    internal U3dViewNodeParameters View { get; set; }

    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; init; }
}

/// <summary>What a view node says of its instance of the view (ECMA-363 9.5.4).</summary>
internal sealed class U3dViewNodeParameters
{
    /// <summary>Three-point perspective, the default projection.</summary>
    internal const uint ThreePointPerspective = 0;

    /// <summary>Orthographic projection.</summary>
    internal const uint Orthographic = 2;

    internal uint Attributes { get; set; }

    /// <summary>The projection bits of <see cref="Attributes"/>: 0 three-point, 2 orthographic, 4 two-point, 6
    /// one-point perspective.</summary>
    internal uint ProjectionMode => Attributes & 0x6;

    internal float NearClip { get; set; }

    internal float FarClip { get; set; }

    /// <summary>The field of view in degrees, for a three-point perspective.</summary>
    internal float FieldOfView { get; set; }

    /// <summary>The height seen, for an orthographic projection.</summary>
    internal float OrthographicHeight { get; set; }

    internal float PortWidth { get; set; }

    internal float PortHeight { get; set; }

    internal uint BackdropCount { get; set; }

    internal uint OverlayCount { get; set; }
}

/// <summary>Which per-vertex attributes beside the position and the normal a shading ID uses (ECMA-363 9.6.1.1.3.9).</summary>
internal sealed class U3dShadingDescription
{
    internal uint Attributes { get; init; }

    /// <summary>The dimensions of each texture layer's coordinates, one entry a layer.</summary>
    internal uint[] TextureCoordinateDimensions { get; init; } = [];

    internal bool HasDiffuseColors => (Attributes & 0x1) != 0;

    internal bool HasSpecularColors => (Attributes & 0x2) != 0;

    internal int TextureLayerCount => TextureCoordinateDimensions.Length;
}

/// <summary>A corner of a face, an end of a line or a point: an index into each of the geometry's pools, -1 for none.</summary>
internal sealed class U3dCorner
{
    internal int Position { get; set; }

    internal int Normal { get; set; } = -1;

    internal int Diffuse { get; set; } = -1;

    internal int Specular { get; set; } = -1;

    /// <summary>One entry a texture layer of the corner's shading ID.</summary>
    internal int[] TextureCoordinates { get; set; } = [];
}

/// <summary>A face (three corners), a line (two) or a point (one), with the shading ID it is drawn by.</summary>
internal sealed class U3dPrimitive
{
    internal int ShadingId { get; init; }

    internal U3dCorner[] Corners { get; init; }
}

/// <summary>A model resource's geometry as the author mesh, point set or line set has it: pools of positions, normals,
/// colours and texture coordinates and the primitives indexing them.</summary>
internal sealed class U3dGeometry
{
    internal U3dGeometryKind Kind { get; init; }

    internal string Name { get; init; }

    /// <summary>A mesh's attributes: 0x1 its faces have no normals.</summary>
    internal uint MeshAttributes { get; set; }

    internal bool ExcludesNormals => Kind == U3dGeometryKind.Mesh && (MeshAttributes & 0x1) != 0;

    internal List<U3dShadingDescription> Shadings { get; } = [];

    internal uint MinimumResolution { get; set; }

    internal uint FinalMaximumResolution { get; set; }

    internal float PositionInverseQuant { get; set; }

    internal float NormalInverseQuant { get; set; }

    internal float TextureCoordinateInverseQuant { get; set; }

    internal float DiffuseColorInverseQuant { get; set; }

    internal float SpecularColorInverseQuant { get; set; }

    internal uint BoneCount { get; set; }

    internal List<Vector3F> Positions { get; } = [];

    internal List<Vector3F> Normals { get; } = [];

    internal List<Vector4F> DiffuseColors { get; } = [];

    internal List<Vector4F> SpecularColors { get; } = [];

    /// <summary>Four coordinates each - u, v, s, t - however many a layer uses.</summary>
    internal List<Vector4F> TextureCoordinates { get; } = [];

    internal List<U3dPrimitive> Primitives { get; } = [];

    /// <summary>The points or line ends at each position, which the next position added is predicted from.</summary>
    internal List<List<U3dCorner>> CornersAtPosition { get; } = [];

    /// <summary>The faces using each position, by their index in <see cref="Primitives"/>: what a mesh's resolution
    /// updates split, move and predict from.</summary>
    internal List<List<int>> FacesAtPosition { get; } = [];

    /// <summary>What the previous new face of a mesh had at each of its corners - split, new, third - which a
    /// duplicate flag of the next one uses again: the diffuse colour, the specular colour, then each texture layer.</summary>
    internal Dictionary<int, int[]> PreviousNewFaceAttributes { get; } = [];

    /// <summary>The last diffuse colour added, which a duplicate flag uses again; -1 before the first.</summary>
    internal int LastDiffuse { get; set; } = -1;

    internal int LastSpecular { get; set; } = -1;

    internal int LastTextureCoordinate { get; set; } = -1;
}

/// <summary>A shading modifier's shader lists (ECMA-363 9.7.5): list i draws the renderable element of shading ID i.</summary>
internal sealed class U3dShadingModifier
{
    /// <summary>The groups the shading is for: 0x1 meshes, 0x2 lines, 0x4 points, 0x8 glyphs.</summary>
    internal uint Attributes { get; init; }

    internal List<string[]> ShaderLists { get; } = [];
}

/// <summary>A texture layer of a lit texture shader (ECMA-363 9.8.3.10).</summary>
internal sealed class U3dTextureLayer
{
    internal string TextureName { get; init; }

    internal float Intensity { get; init; }

    internal byte BlendFunction { get; init; }

    internal byte TextureMode { get; init; }

    internal Matrix4x4F TextureTransform { get; init; }

    internal byte Repeat { get; init; }
}

/// <summary>A lit texture shader (ECMA-363 9.8.3).</summary>
internal sealed class U3dShader
{
    /// <summary>Lit by the scene's lights.</summary>
    internal const uint LightingEnabled = 0x1;

    /// <summary>Pixels failing the alpha test are not drawn.</summary>
    internal const uint AlphaTestEnabled = 0x2;

    /// <summary>The rendered alpha must be greater than the reference.</summary>
    internal const uint AlphaGreater = 0x612;

    /// <summary>The rendered alpha must be at least the reference.</summary>
    internal const uint AlphaGreaterOrEqual = 0x616;

    /// <summary>Blended into the frame by its alpha, the default.</summary>
    internal const uint AlphaBlend = 0x606;

    internal string Name { get; init; }

    internal uint Attributes { get; init; }

    internal float AlphaTestReference { get; init; }

    internal uint AlphaTestFunction { get; init; }

    internal uint ColorBlendFunction { get; init; }

    /// <summary>The model's texture layers the shader uses, one bit a layer.</summary>
    internal uint ShaderChannels { get; init; }

    internal string MaterialName { get; init; }

    internal List<U3dTextureLayer> Layers { get; } = [];

    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; init; }
}

/// <summary>A material resource (ECMA-363 9.8.4).</summary>
internal sealed class U3dMaterial
{
    internal const uint AmbientFlag = 0x1;
    internal const uint DiffuseFlag = 0x2;
    internal const uint SpecularFlag = 0x4;
    internal const uint EmissiveFlag = 0x8;
    internal const uint ReflectivityFlag = 0x10;
    internal const uint OpacityFlag = 0x20;

    internal string Name { get; init; }

    /// <summary>Which of the values are enabled, one flag each.</summary>
    internal uint Attributes { get; init; }

    internal Vector3F AmbientColor { get; init; }

    internal Vector3F DiffuseColor { get; init; }

    internal Vector3F SpecularColor { get; init; }

    internal Vector3F EmissiveColor { get; init; }

    internal float Reflectivity { get; init; }

    internal float Opacity { get; init; }
}

/// <summary>A continuation image of a texture (ECMA-363 9.8.5.1.4) and the bytes its continuation blocks brought.</summary>
internal sealed class U3dTextureImage
{
    internal const byte Jpeg24 = 0x01;
    internal const byte Png = 0x02;
    internal const byte Jpeg8 = 0x03;
    internal const byte Tiff = 0x04;

    /// <summary>The channel bits of a colour image: blue, green, red, luminance.</summary>
    internal const byte ColorChannels = 0x1E;

    internal byte CompressionType { get; init; }

    /// <summary>The channels the image gives: 0x1 alpha, 0x2 blue, 0x4 green, 0x8 red, 0x10 luminance.</summary>
    internal byte Channels { get; init; }

    internal bool IsExternal { get; init; }

    internal uint ByteCount { get; init; }

    internal List<string> Urls { get; } = [];

    internal List<byte[]> Chunks { get; } = [];
}

/// <summary>A texture resource (ECMA-363 9.8.5).</summary>
internal sealed class U3dTexture
{
    internal string Name { get; init; }

    internal uint Width { get; init; }

    internal uint Height { get; init; }

    internal List<U3dTextureImage> Images { get; } = [];

    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; init; }
}

/// <summary>A light resource (ECMA-363 9.8.1).</summary>
internal sealed class U3dLightResource
{
    internal const uint Enabled = 0x1;

    internal string Name { get; init; }

    internal uint Attributes { get; init; } = Enabled;

    /// <summary>0 ambient, 1 directional, 2 point, 3 spot.</summary>
    internal byte Type { get; init; }

    internal Vector3F Color { get; init; }

    internal float ConstantAttenuation { get; init; }

    internal float LinearAttenuation { get; init; }

    internal float QuadraticAttenuation { get; init; }

    internal float SpotAngle { get; init; }

    internal float Intensity { get; init; }
}

/// <summary>A view resource (ECMA-363 9.8.2): what of its passes the import looks at.</summary>
internal sealed class U3dViewResource
{
    internal string Name { get; init; }

    internal uint PassCount { get; init; }

    /// <summary>The node the first pass renders, the world when empty.</summary>
    internal string RootNodeName { get; init; } = string.Empty;

    internal bool FogEnabled { get; init; }
}

/// <summary>The extension a New Object Type block declared (ECMA-363 9.4.5), which its blocks are told by.</summary>
internal sealed class U3dExtension
{
    internal string Name { get; init; }

    internal string Vendor { get; init; }

    internal Guid Id { get; init; }
}
