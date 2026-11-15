namespace Adamantium.Engine.Compiler.Models.Prc;

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
