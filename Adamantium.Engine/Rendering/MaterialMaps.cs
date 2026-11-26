using System.Runtime.InteropServices;

namespace Adamantium.Engine.Rendering;

/// <summary>A copy's maps beside its color, by their index in the resource heap: the shaders' <c>maps</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct MaterialMaps(uint MetallicRoughness, uint Normal, uint Occlusion, uint Emissive)
{
    /// <summary>No maps at all.</summary>
    public static readonly MaterialMaps None =
        new(MeshInstance.NoMap, MeshInstance.NoMap, MeshInstance.NoMap, MeshInstance.NoMap);
}
