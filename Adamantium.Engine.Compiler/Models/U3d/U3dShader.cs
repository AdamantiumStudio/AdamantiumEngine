using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

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
