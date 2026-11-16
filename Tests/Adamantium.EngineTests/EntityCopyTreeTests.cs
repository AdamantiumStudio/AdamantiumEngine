using System.Collections.Generic;
using System.Linq;
using Adamantium.ECS;
using Adamantium.ECS.Components;
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

    // A skin on one root bends with a joint standing on another: the copy bends with the joint's copy, not the original.
    [Test]
    public void TreesCopiedTogether_NameEachOthersCopies()
    {
        var body = new Entity(null, "Body");
        var rig = new Entity(null, "Rig");
        var joint = new Entity(rig, "Hip");
        var skin = new Skin();
        skin.Joints.Add(joint);
        skin.InverseBindMatrices.Add(Matrix4x4F.Identity);
        body.AddComponent(skin);
        var copies = new Dictionary<Entity, Entity>();

        var copied = Entity.CopyTrees([body, rig], copies);

        Assert.Multiple(() =>
        {
            Assert.That(copied.Select(root => root.Name), Is.EqualTo(new[] { "Body", "Rig" }));
            Assert.That(copies[body].GetComponent<Skin>().Joints, Is.EqualTo(new[] { copies[joint] }));
        });
    }

    // A game component following another entity follows its copy in the game; one outside the trees stays named.
    [Test]
    public void AGameComponentNamingAnEntity_NamesItsCopy()
    {
        var moon = new Entity(null, "Moon");
        var earth = new Entity(null, "Earth");
        var outside = new Entity(null, "Outside");
        moon.AddComponent(new Following { Target = earth, Watched = outside });
        var copies = new Dictionary<Entity, Entity>();

        Entity.CopyTrees([moon, earth], copies);

        var following = copies[moon].GetComponent<Following>();
        Assert.Multiple(() =>
        {
            Assert.That(following.Target, Is.SameAs(copies[earth]));
            Assert.That(following.Watched, Is.SameAs(outside));
            Assert.That(following.Owner, Is.SameAs(copies[moon]));
            Assert.That(moon.GetComponent<Following>().Target, Is.SameAs(earth));
        });
    }

    // Kept back from a game, an entity that went with the game names none.
    [Test]
    public void AnEntityMappedToNone_IsNamedByNone()
    {
        var gone = new Entity(null, "Gone");
        var following = new Following { Target = gone };
        var kept = new Following();

        following.CloneReferences(kept, new Dictionary<Entity, Entity> { [gone] = null });

        Assert.That(kept.Target, Is.Null);
    }

    private sealed class Following : GameComponent
    {
        public Entity Target { get; set; }

        public Entity Watched { get; set; }
    }
}
