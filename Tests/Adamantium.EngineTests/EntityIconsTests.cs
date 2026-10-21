using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Tools;
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
        universe.Satellites.Add(new Selection());
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

    private static Entity CameraEntity(string name) =>
        new CameraTemplate().BuildEntity(null, name, Vector3.Zero, Vector3.ForwardLH, -Vector3.Up, 800, 600, 0.1f, 1000f);
}
