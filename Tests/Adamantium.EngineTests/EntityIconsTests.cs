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

    // An icon is all there is of an entity that shows nothing: whatever it holds, so long as no mesh is on it or under it.
    [Test]
    public void AnEntityWithNoMesh_OnItOrUnderIt_HasItsIcon_WhateverItHolds_AGroupToo()
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
        var withMesh = Marked(new Entity(null, "Shown"));
        withMesh.AddComponent(Shape());
        var withMeshUnder = Marked(new Entity(null, "Holder"));
        new Entity(new Entity(withMeshUnder, "Frame"), "Wheel").AddComponent(Shape());
        var withHiddenMesh = Marked(new Entity(null, "Hidden"));
        var hidden = Shape();
        hidden.IsEnabled = false;
        withHiddenMesh.AddComponent(hidden);
        var withMeshTurnedOff = Marked(new Entity(null, "Off"));
        var off = new Entity(withMeshTurnedOff, "Wheel") { IsEnabled = false };
        off.AddComponent(Shape());
        var withNothingToDraw = Marked(new Entity(null, "Unloaded"));
        withNothingToDraw.AddComponent(new MeshData { Mesh = new Mesh() });
        var start = new Entity(null, "Start");
        start.AddComponent(new SpawnPoint());
        start.AddComponent(new Material());
        var markedStart = Marked(new Entity(null, "Marked start"));
        markedStart.AddComponent(new SpawnPoint());
        var group = Marked(new Entity(null, "Group") { IconKey = EntityIconKeys.Group });
        _ = new Entity(group, "Member");
        var groupOfModels = Marked(new Entity(null, "Fleet") { IconKey = EntityIconKeys.Group });
        new Entity(groupOfModels, "Ship").AddComponent(Shape());
        var groupOfHidden = Marked(new Entity(null, "Docked") { IconKey = EntityIconKeys.Group });
        new Entity(groupOfHidden, "Ship") { Visible = false }.AddComponent(Shape());
        var hiddenGroup = Marked(new Entity(null, "Hidden fleet") { IconKey = EntityIconKeys.Group, Visible = false });
        new Entity(hiddenGroup, "Ship").AddComponent(Shape());
        universe.EntityWorld.EntityManager.AddEntity(own);
        foreach (var entity in new[]
                 {
                     empty, withComponent, withChild, withMesh, withMeshUnder, withHiddenMesh, withMeshTurnedOff,
                     withNothingToDraw, start, markedStart, group, groupOfModels, groupOfHidden, hiddenGroup
                 })
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
            Assert.That(icons.IconOf(withComponent), Is.Not.Null, "a component of its own, nothing to see");
            Assert.That(icons.IconOf(withChild), Is.Not.Null, "an entity under it with nothing to see");
            Assert.That(icons.IconOf(withMesh), Is.Null, "seen by its own mesh");
            Assert.That(icons.IconOf(withMeshUnder), Is.Null, "seen by a mesh deeper down");
            Assert.That(icons.IconOf(withHiddenMesh), Is.Not.Null, "a mesh turned off shows nothing");
            Assert.That(icons.IconOf(withMeshTurnedOff), Is.Not.Null, "a mesh on an entity turned off shows nothing");
            Assert.That(icons.IconOf(withNothingToDraw), Is.Not.Null, "a mesh with no points draws nothing");
            Assert.That(icons.IconOf(start), Is.Not.Null, "a spawn point, game components on it");
            Assert.That(icons.IconOf(markedStart), Is.Not.Null, "a marked spawn point: one icon");
            Assert.That(icons.IconOf(group)?.Name, Is.EqualTo("Group"), "a group of nothing to see, by a group's icon");
            Assert.That(icons.IconOf(groupOfModels), Is.Null, "a group its meshes show");
            Assert.That(icons.IconOf(groupOfHidden)?.Name, Is.EqualTo("Group"), "a group whose meshes are hidden");
            Assert.That(icons.IconOf(hiddenGroup), Is.Null, "a group hidden itself pops no icon up");
        });
    }

    // Asked again each frame: a mesh hung under it takes the icon away, a hidden one gives it back; a marker turned off
    // hides a spawn point's icon too, and the select tool drags by the icon what has one.
    [Test]
    public void AnIcon_FollowsTheMeshesFrameByFrame_AndAMarkerTurnedOff_HidesASpawnPoints()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var holder = Marked(new Entity(null, "Holder"));
        var start = new Entity(null, "Start");
        start.AddComponent(new SpawnPoint());
        var offStart = new Entity(null, "Off start");
        offStart.AddComponent(new SpawnPoint());
        offStart.AddComponent(new Marker { IsEnabled = false });
        universe.EntityWorld.EntityManager.AddEntity(own);
        foreach (var entity in new[] { holder, start, offStart })
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
        var bare = icons.IconOf(holder);
        var wheel = new Entity(holder, "Wheel");
        wheel.AddComponent(Shape());
        tools.Update(new AppTime());
        var shown = icons.IconOf(holder);
        wheel.Visible = false;
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(bare, Is.Not.Null, "nothing under it yet");
            Assert.That(shown, Is.Null, "a mesh hung under it is seen");
            Assert.That(icons.IconOf(holder), Is.Not.Null, "its mesh hidden, nothing is seen of it");
            Assert.That(icons.IconOf(offStart), Is.Null, "its marker turned off");
            Assert.That((EntityIcons.IsMarked(start), EntityIcons.IsMarked(offStart)), Is.EqualTo((true, false)));
        });
    }

    // Asked for, an icon shows over the meshes of a group or a marked entity; left out again, the meshes hide it.
    [Test]
    public void AnIconAskedFor_ShowsOverTheMeshes_TillItIsLeftOut()
    {
        var universe = new TestUniverse();
        var output = new TestOutput();
        universe.Add(output);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        var own = CameraEntity("Own");
        var fleet = Marked(new Entity(null, "Fleet") { IconKey = EntityIconKeys.Group });
        new Entity(fleet, "Ship").AddComponent(Shape());
        var display = new EditorDisplay { ShowsIcon = true };
        fleet.AddComponent(display);
        var shown = Marked(new Entity(null, "Shown"));
        shown.AddComponent(Shape());
        shown.AddComponent(new EditorDisplay { ShowsIcon = true });
        var hidden = Marked(new Entity(null, "Hidden fleet") { IconKey = EntityIconKeys.Group, Visible = false });
        hidden.AddComponent(new EditorDisplay { ShowsIcon = true });
        var convoy = new Entity(null, "Convoy") { Visible = false };
        var underHidden = Marked(new Entity(convoy, "Escort") { IconKey = EntityIconKeys.Group });
        new Entity(underHidden, "Ship").AddComponent(Shape());
        underHidden.AddComponent(new EditorDisplay { ShowsIcon = true });
        var hiddenLamp = new LightTemplate().BuildEntity(convoy, "Lamp", LightType.Point);
        universe.EntityWorld.EntityManager.AddEntity(own);
        foreach (var entity in new[] { fleet, shown, hidden, convoy })
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
        var asked = icons.IconOf(fleet)?.Name;
        display.ShowsIcon = false;
        tools.Update(new AppTime());
        var leftOut = icons.IconOf(fleet);
        display.ShowsIcon = true;
        display.IsEnabled = false;
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo("Group"));
            Assert.That(leftOut, Is.Null, "its meshes show it");
            Assert.That(icons.IconOf(fleet), Is.Null, "the display turned off asks for nothing");
            Assert.That(icons.IconOf(shown), Is.Not.Null, "a marked entity with its own mesh");
            Assert.That(icons.IconOf(hidden), Is.Null, "hidden, asked or not");
            Assert.That(icons.IconOf(underHidden), Is.Null, "an owner hidden");
            Assert.That(icons.IconOf(hiddenLamp), Is.Null, "a light, an owner hidden");
            Assert.That((EntityIcons.CanAskForIcon(fleet), EntityIcons.CanAskForIcon(shown)), Is.EqualTo((true, true)));
            Assert.That(EntityIcons.CanAskForIcon(hiddenLamp), Is.False, "a light always shows its icon");
            Assert.That(EntityIcons.CanAskForIcon(convoy), Is.False, "not marked");
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

    // A mesh with something to draw: a triangle.
    private static MeshData Shape()
    {
        var mesh = new Mesh();
        mesh.SetPoints(new List<Vector3<double>> { Vector3<double>.Zero, Vector3<double>.UnitX, Vector3<double>.UnitY });
        return new MeshData { Mesh = mesh };
    }

    private static Entity CameraEntity(string name) =>
        new CameraTemplate().BuildEntity(null, name, WorldPosition.Zero, Vector3<double>.ForwardLH, -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
}
