using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A model's body edit, written to its file, starts as a rigid body does, so an edit left as it was builds the
/// body a new one would be.</summary>
[TestFixture]
public class BodyEditsTests
{
    [Test]
    public void ABodyEdit_StartsAsARigidBodyDoes()
    {
        var edit = new BodyEdits();
        var body = new RigidBody();

        Assert.Multiple(() =>
        {
            Assert.That(edit.Kind, Is.EqualTo(body.Kind));
            Assert.That(edit.Mass, Is.EqualTo(body.Mass));
            Assert.That(edit.Density, Is.EqualTo(body.Density));
            Assert.That(edit.MaterialPath, Is.EqualTo(body.MaterialPath));
            Assert.That(edit.Layer, Is.EqualTo(body.Layer));
            Assert.That(edit.LinearDamping, Is.EqualTo(body.LinearDamping));
            Assert.That(edit.AngularDamping, Is.EqualTo(body.AngularDamping));
            Assert.That(edit.GravityScale, Is.EqualTo(body.GravityScale));
            Assert.That(edit.CanSleep, Is.EqualTo(body.CanSleep));
        });
    }
}
