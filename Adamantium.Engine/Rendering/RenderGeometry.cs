using System;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Engine.Rendering;

/// <summary>
/// GPU vertex and index buffers of one geometry in one vertex format. Every mesh with the same points, attributes and
/// indices draws with the same one - the copies of a model loaded twice or duplicated alike - so they can be drawn by one
/// instanced call. Owned by a <see cref="MeshGeometryCache"/>, never by an ECS component, and never changed: a mesh that
/// changes moves to another.
/// </summary>
public sealed class RenderGeometry : DisposableObject
{
    private readonly IBuffer _vertexBuffer;
    private readonly IBuffer _indexBuffer;

    internal RenderGeometry(IBuffer vertexBuffer, IBuffer indexBuffer, Type vertexType, PrimitiveTopology topology)
    {
        _vertexBuffer = vertexBuffer == null ? null : ToDispose(vertexBuffer);
        _indexBuffer = indexBuffer == null ? null : ToDispose(indexBuffer);
        VertexType = vertexType;
        Topology = topology;
    }

    public Type VertexType { get; }
    public IBuffer VertexBuffer => _vertexBuffer;
    public IBuffer IndexBuffer => _indexBuffer;
    public PrimitiveTopology Topology { get; }
    public bool HasIndices => _indexBuffer != null;

    /// <summary>Applies the per-object render state, sets the vertex input, and issues the draw of
    /// <paramref name="instanceCount"/> copies. The caller applies the effect pass first.</summary>
    public void Draw(IGraphicsDevice device, in RenderState state, uint instanceCount = 1)
    {
        if (_vertexBuffer == null || _vertexBuffer.IsDisposed || instanceCount == 0)
        {
            return;
        }

        device.VertexType = VertexType;
        device.PrimitiveTopology = state.TopologyOverride ?? Topology;
        device.PolygonMode = state.WireFrame ? PolygonMode.Line : PolygonMode.Fill;
        device.CullMode = state.CullMode;
        device.DepthTestEnabled = state.DepthTest;
        device.DepthWriteEnable = state.DepthWrite;

        if (HasIndices)
        {
            device.DrawIndexed(_vertexBuffer, _indexBuffer, instanceCount);
        }
        else
        {
            device.SetVertexBuffer(_vertexBuffer);
            device.Draw(_vertexBuffer.ElementCount, instanceCount);
        }
    }
}
