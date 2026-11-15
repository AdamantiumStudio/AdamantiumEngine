using System.Collections.Generic;
using System.Linq;
using Adamantium.ECS;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A tree of entities copied for the game to play: the same names and order, its own components, and what each
/// copy came from.</summary>
[TestFixture]
public class EntityCopyTreeTests
{
    [Test]
    public void ACopiedTree_KeepsItsNamesAndOrder_AndSaysWhatEachCameFrom()
    {
        var root = new Entity(null, "Solar system");
        var sun = new Entity(root, "Sun");
        var earth = new Entity(root, "Earth");
        var moon = new Entity(earth, "Moon");
        earth.AddComponent(new Pushed { Speed = 3 });
        earth.Transform.Position = new Vector3<double>(5, 0, 0);
        var copies = new Dictionary<Entity, Entity>();

        var copy = root.CopyTree(copies);

        var copiedEarth = copies[earth];
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("Solar system"));
            Assert.That(copy.Dependencies.Select(child => child.Name), Is.EqualTo(new[] { "Sun", "Earth" }));
            Assert.That(copies.Keys, Is.EquivalentTo(new[] { root, sun, earth, moon }));
            Assert.That(copies[moon].Owner, Is.SameAs(copiedEarth));
            Assert.That(copiedEarth.GetComponent<Pushed>(), Is.Not.SameAs(earth.GetComponent<Pushed>()));
            Assert.That(copiedEarth.GetComponent<Pushed>().Speed, Is.EqualTo(3));
            Assert.That(copiedEarth.Transform.Position, Is.EqualTo(new Vector3<double>(5, 0, 0)));
        });
    }
}
