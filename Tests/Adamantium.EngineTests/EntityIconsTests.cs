using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Engine.Tools;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class EntityIconsTests
{
    [Test]
    public void AnOutputsOwnCamera_HasNoIcon_ACameraOfTheSceneHasOne()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var scene = CameraEntity("Scene");
        universe.EntityWorld.EntityManager.AddEntity(own);
        universe.EntityWorld.EntityManager.AddEntity(scene);
        output.OwnCamera = own.GetComponent<Camera>();
        output.Camera = output.OwnCamera;
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        var icons = new EntityIcons();
        tools.AttachProcessor(icons);

        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(icons.IconOf(own), Is.Null);
            Assert.That(icons.IconOf(scene), Is.Not.Null);
        });
    }

    [TestCase(EntityIconKinds.Entities)]
    [TestCase(EntityIconKinds.Groups)]
    [TestCase(EntityIconKinds.Lights)]
    [TestCase(EntityIconKinds.Cameras)]
    public void AKindLeftOut_LosesItsIcons_TheOthersKeepTheirs_AndTheyComeBack(EntityIconKinds hidden)
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var kinds = new Dictionary<EntityIconKinds, Entity>
        {
            [EntityIconKinds.Entities] = Marked(new Entity(null, "Entity")),
            [EntityIconKinds.Groups] = Marked(new Entity(null, "Group") { IconKey = EntityIconKeys.Group }),
            // A light with a marker still goes as a light: hidden with the lights, not shown as an empty entity.
            [EntityIconKinds.Lights] = Marked(new LightTemplate().BuildEntity(null, "Light", LightType.Point)),
            [EntityIconKinds.Cameras] = CameraEntity("Scene")
        };
        universe.EntityWorld.EntityManager.AddEntity(own);
        foreach (var entity in kinds.Values)
        {
            universe.EntityWorld.EntityManager.AddEntity(entity);
        }

        output.OwnCamera = own.GetComponent<Camera>();
        output.Camera = output.OwnCamera;
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        var icons = new EntityIcons();
        tools.AttachProcessor(icons);

        icons.Shows = EntityIconKinds.All & ~hidden;
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            foreach (var (kind, entity) in kinds)
            {
                Assert.That(icons.IconOf(entity), kind == hidden ? Is.Null : Is.Not.Null, kind.ToString());
            }
        });

        icons.Shows = EntityIconKinds.All;
        tools.Update(new AppTime());

        Assert.That(icons.IconOf(kinds[hidden]), Is.Not.Null);
    }

    [Test]
    public void AMarkerHoldingAnything_IsNotEmpty_AndHasNoIcon_AGroupKeepsItsOwn()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var empty = Marked(new Entity(null, "Empty"));
        var withComponent = Marked(new Entity(null, "Orbit"));
        withComponent.AddComponent(new Material());
        var withChild = Marked(new Entity(null, "Pivot"));
        _ = new Entity(withChild, "Body");
        var group = Marked(new Entity(null, "Group") { IconKey = EntityIconKeys.Group });
        _ = new Entity(group, "Member");
        universe.EntityWorld.EntityManager.AddEntity(own);
        foreach (var entity in new[] { empty, withComponent, withChild, group })
        {
            universe.EntityWorld.EntityManager.AddEntity(entity);
        }

        output.OwnCamera = own.GetComponent<Camera>();
        output.Camera = output.OwnCamera;
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        var icons = new EntityIcons();
        tools.AttachProcessor(icons);

        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(icons.IconOf(empty), Is.Not.Null, "nothing but its place and its marker");
            Assert.That(icons.IconOf(withComponent), Is.Null, "a component of its own");
            Assert.That(icons.IconOf(withChild), Is.Null, "an entity under it");
            Assert.That(icons.IconOf(group), Is.Not.Null, "a group is shown as a group, whatever it holds");
        });
    }

    [Test]
    public void AnEntityUnderADisabledOne_HasNoIcon_TillItIsEnabled()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var group = Marked(new Entity(null, "Group") { IconKey = EntityIconKeys.Group });
        var lamp = new LightTemplate().BuildEntity(group, "Lamp", LightType.Point);
        universe.EntityWorld.EntityManager.AddEntity(own);
        universe.EntityWorld.EntityManager.AddEntity(group);
        output.OwnCamera = own.GetComponent<Camera>();
        output.Camera = output.OwnCamera;
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        var icons = new EntityIcons();
        tools.AttachProcessor(icons);

        group.IsEnabled = false;
        tools.Update(new AppTime());
        var whileDisabled = icons.IconOf(lamp);
        group.IsEnabled = true;
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(whileDisabled, Is.Null);
            Assert.That(icons.IconOf(lamp), Is.Not.Null);
        });
    }

    [Test]
    public void ALightWhoseKindChanges_ShowsTheIconOfItsNewKind()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var lamp = new LightTemplate().BuildEntity(null, "Lamp", LightType.Point);
        universe.EntityWorld.EntityManager.AddEntity(own);
        universe.EntityWorld.EntityManager.AddEntity(lamp);
        output.OwnCamera = own.GetComponent<Camera>();
        output.Camera = output.OwnCamera;
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        var icons = new EntityIcons();
        tools.AttachProcessor(icons);
        tools.Update(new AppTime());
        var asPoint = icons.IconOf(lamp).Name;

        lamp.GetComponent<Light>().Type = LightType.Directional;
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(asPoint, Is.EqualTo("Point light"));
            Assert.That(icons.IconOf(lamp).Name, Is.EqualTo("Directional light"));
            Assert.That(icons.IconOf(lamp).Dependencies, Has.Count.EqualTo(1), "with its rays");
        });
    }

    private static Entity Marked(Entity entity)
    {
        entity.AddComponent(new Marker());
        return entity;
    }

    private static Entity CameraEntity(string name) =>
        new CameraTemplate().BuildEntity(null, name, Vector3<double>.Zero, Vector3<double>.ForwardLH, -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
}
