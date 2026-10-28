using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates.CameraTemplates;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class IconMeshTests
{
    private static Entity[] Icons() =>
    [
        new PointLightIconTemplate().BuildEntity(null, "Point"),
        new SpotLightIconTemplate().BuildEntity(null, "Spot"),
        new DirectionalLightIconTemplate().BuildEntity(null, "Directional"),
        new CameraIconTemplate().BuildEntity(null, "Camera")
    ];

    [Test]
    public void EveryIcon_TurnsIntoVertices()
    {
        foreach (var icon in Icons())
        {
            var mesh = icon.GetComponent<MeshData>().Mesh;
            Assert.DoesNotThrow(() => mesh.ToMeshVertices(),
                $"{icon.Name}: {mesh.Points.Length} points, {mesh.Normals?.Length ?? 0} normals, semantic {mesh.Semantic}");
        }
    }

    [Test]
    public void DirectionalRays_RunWhereAnUnturnedLightShines_AndPointThatWay()
    {
        var rays = new DirectionalLightVisualTemplate().BuildEntity(null, "Rays").GetComponent<MeshData>().Mesh;
        var shines = new Entity(null, "Light");
        shines.AddComponent(new Light(LightType.Directional));
        var direction = shines.GetComponent<Light>().Direction;

        Assert.DoesNotThrow(() => rays.ToMeshVertices());
        Assert.That(rays.Points.Length, Is.GreaterThan(0));
        for (int i = 0; i < rays.Points.Length; i += 4)
        {
            var tip = rays.Points[i + 1];
            var along = Vector3F.Normalize((tip - rays.Points[i]).As<float>());
            Assert.That(Vector3F.Dot(along, direction), Is.GreaterThan(0.9999f), $"ray {i / 4}");
            Assert.That(Vector3F.Dot((rays.Points[i + 2] - tip).As<float>(), direction), Is.LessThan(0), $"ray {i / 4}, left barb");
            Assert.That(Vector3F.Dot((rays.Points[i + 3] - tip).As<float>(), direction), Is.LessThan(0), $"ray {i / 4}, right barb");
        }
    }
}
