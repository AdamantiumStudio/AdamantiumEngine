using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A shading modifier's shader lists (ECMA-363 9.7.5): list i draws the renderable element of shading ID i.</summary>
internal sealed class U3dShadingModifier
{
    /// <summary>The groups the shading is for: 0x1 meshes, 0x2 lines, 0x4 points, 0x8 glyphs.</summary>
    internal uint Attributes { get; init; }

    internal List<string[]> ShaderLists { get; } = [];
}
