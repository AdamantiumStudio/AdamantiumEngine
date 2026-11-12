extern alias engine;
using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using NUnit.Framework;
using engine::Adamantium.Engine.Rendering;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The cache lets go of the GPU geometry of the meshes no longer drawn, itself: a mesh not drawn for the time it
/// keeps takes its buffers with it, unless a mesh still drawn has the same content; Clear lets go of all.</summary>
[TestFixture]
public class MeshGeometryCacheTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AMeshNotDrawn_LetsItsGeometryGo_AndOneDrawnKeepsIt()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice(), TimeSpan.FromHours(1));
        var gone = Triangle(1);
        var kept = Triangle(2);
        cache.GetOrCreate(gone, typeof(MeshVertex));
        var geometry = cache.GetOrCreate(kept, typeof(MeshVertex));
        using var quick = new MeshGeometryCache(GpuFixture.CreateRenderDevice(), TimeSpan.Zero);
        quick.GetOrCreate(gone, typeof(MeshVertex));

        cache.ReleaseUnused();
        quick.ReleaseUnused();

        Assert.Multiple(() =>
        {
            Assert.That(cache.GeometryCount, Is.EqualTo(2), "drawn within the time kept");
            Assert.That(cache.GetOrCreate(kept, typeof(MeshVertex)), Is.SameAs(geometry));
            Assert.That(quick.GeometryCount, Is.Zero, "not drawn for the time kept");
        });
    }

    [Test]
    public void AMeshLetGo_KeepsTheGeometryAMeshStillHeldShares()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice(), TimeSpan.Zero);
        var first = Triangle(1);
        var second = Triangle(1);
        var shared = cache.GetOrCreate(first, typeof(MeshVertex));
        cache.ReleaseUnused();
        Assert.That(cache.GeometryCount, Is.Zero, "the first let go");

        var again = cache.GetOrCreate(second, typeof(MeshVertex));
        var firstAgain = cache.GetOrCreate(first, typeof(MeshVertex));

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.Not.SameAs(shared), "made anew once let go");
            Assert.That(firstAgain, Is.SameAs(again), "the same content shares one geometry");
            Assert.That(cache.GeometryCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clear_LetsGoOfEveryMesh()
    {
        using var cache = new MeshGeometryCache(GpuFixture.CreateRenderDevice());
        cache.GetOrCreate(Triangle(1), typeof(MeshVertex));
        cache.GetOrCreate(Triangle(2), typeof(MeshVertex));

        cache.Clear();

        Assert.That(cache.GeometryCount, Is.Zero);
    }

    private static Mesh Triangle(double size)
    {
        return new Mesh()
            .SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(size, 0, 0), new Vector3<double>(0, size, 0)])
            .SetIndices([0, 1, 2]);
    }
}
