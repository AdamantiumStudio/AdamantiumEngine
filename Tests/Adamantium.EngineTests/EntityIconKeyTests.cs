using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates.GeometricPrimitives;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.ProceduralGeometry;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>An entity says which icon an editor shows for it, and whatever makes one of a kind says that kind.</summary>
[TestFixture]
public class EntityIconKeyTests
{
    [Test]
    public void APlainEntity_HasTheEntityIcon() =>
        Assert.That(new Entity(null, "Group").IconKey, Is.EqualTo(EntityIconKeys.Entity));

    [TestCase(LightType.Point, EntityIconKeys.PointLight)]
    [TestCase(LightType.Spot, EntityIconKeys.SpotLight)]
    [TestCase(LightType.Directional, EntityIconKeys.DirectionalLight)]
    public void ALight_HasTheIconOfItsKind(LightType type, string key) =>
        Assert.That(new LightTemplate().BuildEntity(null, "Light", type).IconKey, Is.EqualTo(key));

    [Test]
    public void ACamera_HasTheCameraIcon()
    {
        var camera = new CameraTemplate().BuildEntity(null, "Camera", Vector3<double>.Zero, Vector3<double>.ForwardLH, -Vector3<double>.Up,
            800, 600, 0.1f, 1000f);

        Assert.That(camera.IconKey, Is.EqualTo(EntityIconKeys.Camera));
    }

    // The light says its own kind: changed later, or put on an entity no light template made.
    [Test]
    public void ALightWhoseKindChanges_ChangesItsIcon()
    {
        var entity = new LightTemplate().BuildEntity(null, "Light", LightType.Point);

        entity.GetComponent<Light>().Type = LightType.Spot;

        Assert.That(entity.IconKey, Is.EqualTo(EntityIconKeys.SpotLight));
    }

    [Test]
    public void ALightPutOnAnyEntity_GivesItsIcon()
    {
        var entity = new Entity(null, "Lamp");

        entity.AddComponent(new Light(LightType.Directional));

        Assert.That(entity.IconKey, Is.EqualTo(EntityIconKeys.DirectionalLight));
    }

    [Test]
    public void ADuplicate_KeepsItsIcon()
    {
        var light = new LightTemplate().BuildEntity(null, "Light", LightType.Spot);

        Assert.That(light.Duplicate().IconKey, Is.EqualTo(EntityIconKeys.SpotLight));
    }

    [Test]
    public void APrimitive_HasTheMeshIcon()
    {
        var entity = new CubeTemplate(GeometryType.Solid, 1).BuildEntity(new Entity(null, "Cube")).GetAwaiter().GetResult();

        Assert.That(entity.IconKey, Is.EqualTo(EntityIconKeys.Mesh));
    }
}
