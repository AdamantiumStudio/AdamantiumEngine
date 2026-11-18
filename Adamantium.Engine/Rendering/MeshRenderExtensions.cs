using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Engine.Rendering;

// The bridge between a MeshData component and the render layer: maps the component's explicit render config to a vertex
// format + device render state, and draws it through the geometry cache. Centralized so the processor and the icon
// managers share one mapping.
public static class MeshRenderExtensions
{
    public static Type ResolveVertexType(this MeshData data) =>
        data.RenderMode == MeshRenderMode.Skinned ? typeof(SkinnedMeshVertex) : typeof(MeshVertex);

    // Both faces: the tools' and gizmos' meshes this draws have no material to say otherwise.
    public static RenderState ToRenderState(this MeshData data) =>
        new(data.IsWireFrame, CullModeFlagBits.None, data.DepthTestEnabled, data.DepthWriteEnabled, data.TopologyOverride);

    /// <summary>What a material's faces cull, placed by <paramref name="world"/>: the back ones for its front, the front
    /// ones for its back, none for both or without a material. A mirroring placement turns its triangles the other way,
    /// so the sides swap.</summary>
    public static CullModeFlagBits CullModeOf(Material material, in Matrix4x4F world)
    {
        var mirrored = world.Determinant() < 0;
        return material?.Faces switch
        {
            MaterialFaces.Front => mirrored ? CullModeFlagBits.FrontBit : CullModeFlagBits.BackBit,
            MaterialFaces.Back => mirrored ? CullModeFlagBits.BackBit : CullModeFlagBits.FrontBit,
            _ => CullModeFlagBits.None
        };
    }

    // Draws the mesh through the cache. The caller applies the effect pass first (the pass family follows the same
    // RenderMode this uses to pick the vertex format). instanceCount above 1 needs a pass that reads the per-instance
    // tables - the caller fills those before applying it.
    public static void DrawMesh(this MeshGeometryCache cache, IGraphicsDevice device, MeshData data,
        uint instanceCount = 1)
    {
        if (data?.Mesh == null) return;
        cache.GetOrCreate(data.Mesh, data.ResolveVertexType()).Draw(device, data.ToRenderState(), instanceCount);
    }
}
