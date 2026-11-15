namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A material of the globals: a generic material's color indices and alphas (PRC_TYPE_GRAPH_Material), or a
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
