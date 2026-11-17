using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Rendering;

/// <summary>One drawn copy of a mesh as a lit pass reads it from the instance buffer: where it stands, its color, and
/// the palette of its joints when it is skinned. Laid out as the shaders' <c>MeshInstance</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MeshInstance
{
    /// <summary>Where the copy stands, as the camera sees the world.</summary>
    public readonly Matrix4x4F World;

    /// <summary>Its color where it has no texture, and its alpha, which a texture's alpha multiplies.</summary>
    public readonly Vector4F Albedo;

    /// <summary>The first matrix of its joints' palette in the palette buffer.</summary>
    public readonly uint PaletteFirst;

    /// <summary>How many joints its palette holds; 0 for a mesh drawn unbent.</summary>
    public readonly uint PaletteCount;

    /// <summary>Its texture's index in the resource heap seen as an array of textures; a textured pass only.</summary>
    public readonly uint Texture;

    /// <summary>Below this alpha an opaque pass leaves a pixel out; 0 keeps every pixel.</summary>
    public readonly float AlphaCutoff;

    public MeshInstance(Matrix4x4F world, Vector4F albedo, uint paletteFirst, uint paletteCount, uint texture,
        float alphaCutoff)
    {
        World = world;
        Albedo = albedo;
        PaletteFirst = paletteFirst;
        PaletteCount = paletteCount;
        Texture = texture;
        AlphaCutoff = alphaCutoff;
    }
}
