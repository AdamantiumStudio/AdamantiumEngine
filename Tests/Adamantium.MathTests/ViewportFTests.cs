using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>A viewport projects as the engine's clip space runs - y down, as Vulkan's - and unprojects back to the point
/// it came from; a pick ray through the pixel a point lands on passes through the point.</summary>
[TestFixture]
public class ViewportFTests
{
    private static readonly ViewportF Viewport = new(10, 20, 200, 100, 0, 1);

    [Test]
    public void TheTopOfClipSpace_LandsAtTheTopOfTheViewport()
    {
        var top = Viewport.Project(new Vector3F(-1, -1, 0.5f), Matrix4x4F.Identity);

        Assert.That((top.X, top.Y, top.Z), Is.EqualTo((10f, 20f, 0.5f)));
    }

    [Test]
    public void APickRayThroughWhereAPointLands_PassesThroughThePoint()
    {
        var viewProjection = Matrix4x4.CreateTranslation<float>(0, 0, -5) * Matrix4x4.CreatePerspectiveFovY<float>(1f, 2f, 0.1f, 100f);
        var point = new Vector3F(0.3f, 0.7f, 1.2f);
        var pixel = Viewport.Project(point, viewProjection);

        var ray = Ray.CreateFromViewport(MathF.Round(pixel.X), MathF.Round(pixel.Y), Viewport, viewProjection);
        var along = Vector3F.Dot(point - ray.Position, ray.Direction);
        var miss = (ray.Position + ray.Direction * along - point).Length();

        Assert.That(miss, Is.LessThan(0.05f));
    }

    [Test]
    public void AProjectedPoint_UnprojectsToItself()
    {
        var projection = Matrix4x4.CreatePerspectiveFovRH<float>(1f, 2f, 0.1f, 100f);
        var view = Matrix4x4.CreateTranslation<float>(0, 0, -5);
        var point = new Vector3F(0.3f, -0.7f, 1.2f);

        var back = Viewport.Unproject(Viewport.Project(point, view * projection), view * projection);

        Assert.That(back.X, Is.EqualTo(point.X).Within(1e-3f));
        Assert.That(back.Y, Is.EqualTo(point.Y).Within(1e-3f));
        Assert.That(back.Z, Is.EqualTo(point.Z).Within(1e-3f));
    }
}
