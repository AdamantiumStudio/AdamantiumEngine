using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Hashing;
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
/// instanced call. A mesh is hashed again only when its version moves. The cache owns the buffers and lets them go
/// itself: <see cref="ReleaseUnused"/>, called once a frame by whoever draws, drops the meshes not drawn for
/// <see cref="KeepUnused"/>, and a buffer goes once no mesh kept shares it and no frame in flight draws with it;
/// <see cref="Clear"/> drops every mesh at once. Owned and disposed by the processor that draws with it, and used from its
/// thread alone.
/// </summary>
public sealed class MeshGeometryCache : DisposableObject
{
    /// <summary>How long a mesh not drawn keeps its geometry unless told otherwise.</summary>
    public static readonly TimeSpan DefaultKeepUnused = TimeSpan.FromSeconds(5);

    private readonly IGraphicsDevice _device;
    private readonly Dictionary<Mesh, MeshGeometries> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Mesh> _unused = [];
    private readonly Dictionary<ContentKey, (RenderGeometry Geometry, int Users)> _contents = new();
    private readonly Dictionary<RenderGeometry, ContentKey> _contentOf = new();

    public MeshGeometryCache(IGraphicsDevice device) : this(device, DefaultKeepUnused)
    {
    }

    public MeshGeometryCache(IGraphicsDevice device, TimeSpan keepUnused)
    {
        _device = device;
        KeepUnused = keepUnused;
    }

    /// <summary>How long a mesh not drawn keeps its geometry.</summary>
    public TimeSpan KeepUnused { get; }

    /// <summary>How many sets of buffers the cache holds: one for each content its meshes have.</summary>
    public int GeometryCount => _contents.Count;

    /// <summary>The geometry <paramref name="mesh"/> draws with in <paramref name="vertexType"/>: the one of its content,
    /// made when no other mesh has that content yet. The mesh counts as drawn now.</summary>
    public RenderGeometry GetOrCreate(Mesh mesh, Type vertexType)
    {
        if (!_meshes.TryGetValue(mesh, out var geometries))
        {
            geometries = new MeshGeometries();
            _meshes.Add(mesh, geometries);
        }

        geometries.LastDrawn = Stopwatch.GetTimestamp();
        var known = geometries.Find(vertexType);
        if (known.Geometry != null && known.Version == mesh.Version)
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

        geometries.Set(vertexType, mesh.Version, geometry);
        return geometry;
    }

    /// <summary>Lets go of the geometry of the meshes not drawn for <see cref="KeepUnused"/>: called once a frame by
    /// whoever draws, in a frame that draws no mesh too.</summary>
    public void ReleaseUnused()
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var (mesh, geometries) in _meshes)
        {
            if (Stopwatch.GetElapsedTime(geometries.LastDrawn, now) >= KeepUnused)
            {
                _unused.Add(mesh);
            }
        }

        foreach (var mesh in _unused)
        {
            Forget(mesh);
        }

        _unused.Clear();
    }

    /// <summary>Lets go of the geometry of every mesh.</summary>
    public void Clear()
    {
        _unused.AddRange(_meshes.Keys);
        foreach (var mesh in _unused)
        {
            Forget(mesh);
        }

        _unused.Clear();
    }

    private void Forget(Mesh mesh)
    {
        if (!_meshes.Remove(mesh, out var geometries))
        {
            return;
        }

        foreach (var geometry in geometries.All)
        {
            Release(geometry);
        }
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
        if (!_contentOf.TryGetValue(geometry, out var content))
        {
            return;
        }

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

    private sealed class MeshGeometries
    {
        private readonly List<(Type VertexType, int Version, RenderGeometry Geometry)> _formats = new(1);

        public long LastDrawn { get; set; }

        public IEnumerable<RenderGeometry> All
        {
            get
            {
                foreach (var (_, _, geometry) in _formats)
                {
                    yield return geometry;
                }
            }
        }

        public (int Version, RenderGeometry Geometry) Find(Type vertexType)
        {
            foreach (var (type, version, geometry) in _formats)
            {
                if (type == vertexType)
                {
                    return (version, geometry);
                }
            }

            return default;
        }

        public void Set(Type vertexType, int version, RenderGeometry geometry)
        {
            for (var i = 0; i < _formats.Count; i++)
            {
                if (_formats[i].VertexType == vertexType)
                {
                    _formats[i] = (vertexType, version, geometry);
                    return;
                }
            }

            _formats.Add((vertexType, version, geometry));
        }
    }

    private readonly record struct ContentKey(
        UInt128 Hash,
        Type VertexType,
        PrimitiveTopology Topology,
        int Vertices,
        int Indices);
}
