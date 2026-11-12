extern alias engine;
using System;
using System.Runtime.CompilerServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using NUnit.Framework;
using engine::Adamantium.Engine.Rendering;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The GPU geometry of meshes lives as long as the meshes: a mesh gone takes its buffers with it, unless a mesh
/// still alive has the same content.</summary>
[TestFixture]
public class MeshGeometryCacheTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AMeshCollected_LetsItsGeometryGo()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice());
        DrawOnce(cache, 1);
        var kept = Triangle(2);
        cache.GetOrCreate(kept, typeof(MeshVertex));

        Collect();
        cache.GetOrCreate(kept, typeof(MeshVertex));

        Assert.That(cache.GeometryCount, Is.EqualTo(1));
        GC.KeepAlive(kept);
    }

    [Test]
    public void AMeshCollected_KeepsTheGeometryAMeshAliveShares()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice());
        var kept = Triangle(1);
        var geometry = cache.GetOrCreate(kept, typeof(MeshVertex));
        DrawOnce(cache, 1);

        Collect();

        Assert.Multiple(() =>
        {
            Assert.That(cache.GetOrCreate(kept, typeof(MeshVertex)), Is.SameAs(geometry));
            Assert.That(cache.GeometryCount, Is.EqualTo(1));
        });
        GC.KeepAlive(kept);
    }

    [Test]
    public void AMeshAlive_KeepsItsGeometry()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice());
        var kept = Triangle(1);
        var geometry = cache.GetOrCreate(kept, typeof(MeshVertex));

        Collect();

        Assert.That(cache.GetOrCreate(kept, typeof(MeshVertex)), Is.SameAs(geometry));
        GC.KeepAlive(kept);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrawOnce(MeshGeometryCache cache, double size)
    {
        cache.GetOrCreate(Triangle(size), typeof(MeshVertex));
    }

    private static Mesh Triangle(double size)
    {
        return new Mesh()
            .SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(size, 0, 0), new Vector3<double>(0, size, 0)])
            .SetIndices([0, 1, 2]);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
