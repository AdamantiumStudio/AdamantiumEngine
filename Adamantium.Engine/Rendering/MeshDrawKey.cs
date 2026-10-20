using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;

namespace Adamantium.Engine.Rendering;

/// <summary>What copies of a mesh must share to be drawn by one instanced call: the geometry, the pass and the render
/// state. Their textures may differ - each copy names its own.</summary>
public readonly record struct MeshDrawKey(
    RenderGeometry Geometry,
    IEffectPass Pass,
    CullModeFlagBits CullMode,
    bool DepthTest,
    bool DepthWrite,
    bool WireFrame,
    PrimitiveTopology? Topology)
{
    /// <summary>The render state the copies are drawn with.</summary>
    public RenderState State => new(WireFrame, CullMode, DepthTest, DepthWrite, Topology);
}
