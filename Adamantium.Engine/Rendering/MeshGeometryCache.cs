using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.Engine.Rendering;

/// <summary>
/// The render layer's GPU geometry. A mesh in a vertex format maps to the geometry of its content: meshes with the same
/// vertices and indices share one set of buffers, found by a hash of what would be uploaded, so their copies draw as one
/// instanced call. A mesh is hashed again only when its version moves; a geometry no mesh uses any more is let go once no
/// frame in flight draws with it. Owned and disposed by the processor that draws with it.
/// </summary>
public sealed class MeshGeometryCache : DisposableObject
{
    private readonly IGraphicsDevice _device;
    private readonly Dictionary<MeshKey, (int Version, RenderGeometry Geometry)> _meshes = new();
    private readonly Dictionary<ContentKey, (RenderGeometry Geometry, int Users)> _contents = new();
    private readonly Dictionary<RenderGeometry, ContentKey> _contentOf = new();

    public MeshGeometryCache(IGraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>The geometry <paramref name="mesh"/> draws with in <paramref name="vertexType"/>: the one of its content,
    /// made when no other mesh has that content yet.</summary>
    public RenderGeometry GetOrCreate(Mesh mesh, Type vertexType)
    {
        var key = new MeshKey(mesh, vertexType);
        if (_meshes.TryGetValue(key, out var known) && known.Version == mesh.Version)
        {
            return known.Geometry;
        }

        var geometry = vertexType == typeof(SkinnedMeshVertex)
            ? Share(mesh.HasPoints ? mesh.ToSkinnedMeshVertices() : [], mesh, vertexType)
            : Share(mesh.HasPoints ? mesh.ToMeshVertices() : [], mesh, vertexType);
        if (known.Geometry != null)
        {
            Release(known.Geometry);
        }

        _meshes[key] = (mesh.Version, geometry);
        return geometry;
    }

    private RenderGeometry Share<T>(T[] vertices, Mesh mesh, Type vertexType) where T : struct
    {
        int[] indices = mesh.HasPoints && mesh.HasIndices ? mesh.Indices : [];
        var hash = new XxHash128();
        hash.Append(MemoryMarshal.AsBytes(vertices.AsSpan()));
        hash.Append(MemoryMarshal.AsBytes(indices.AsSpan()));
        var content = new ContentKey(hash.GetCurrentHashAsUInt128(), vertexType, mesh.MeshTopology, vertices.Length,
            indices.Length);
        if (_contents.TryGetValue(content, out var shared))
        {
            _contents[content] = (shared.Geometry, shared.Users + 1);
            return shared.Geometry;
        }

        var vertexBuffer = vertices.Length > 0 ? Buffer.Vertex.New(_device, vertices, BufferMemoryUsage.GpuOnly) : null;
        var indexBuffer = indices.Length > 0 ? Buffer.Index.New(_device, indices, BufferMemoryUsage.GpuOnly) : null;
        var geometry = new RenderGeometry(vertexBuffer, indexBuffer, vertexType, mesh.MeshTopology);
        _contents.Add(content, (geometry, 1));
        _contentOf.Add(geometry, content);
        return geometry;
    }

    private void Release(RenderGeometry geometry)
    {
        var content = _contentOf[geometry];
        var (_, users) = _contents[content];
        if (users > 1)
        {
            _contents[content] = (geometry, users - 1);
            return;
        }

        _contents.Remove(content);
        _contentOf.Remove(geometry);
        _device.AddToDeferDisposeQueue(geometry);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            foreach (var (geometry, _) in _contents.Values)
            {
                geometry.Dispose();
            }

            _contents.Clear();
            _contentOf.Clear();
            _meshes.Clear();
        }

        base.Dispose(disposeManagedResources);
    }

    // Reference identity on the mesh, so a replaced Mesh instance is a fresh entry; the vertex format apart, so a mesh
    // drawn both skinned and static has a geometry for each.
    private readonly struct MeshKey : IEquatable<MeshKey>
    {
        private readonly Mesh _mesh;
        private readonly Type _vertexType;

        public MeshKey(Mesh mesh, Type vertexType)
        {
            _mesh = mesh;
            _vertexType = vertexType;
        }

        public bool Equals(MeshKey other) => ReferenceEquals(_mesh, other._mesh) && _vertexType == other._vertexType;
        public override bool Equals(object obj) => obj is MeshKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(_mesh), _vertexType);
    }

    private readonly record struct ContentKey(
        UInt128 Hash,
        Type VertexType,
        PrimitiveTopology Topology,
        int Vertices,
        int Indices);
}
