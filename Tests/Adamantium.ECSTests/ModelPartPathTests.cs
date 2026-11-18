using Adamantium.ECS;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A part of a model is found again by the path of names from the model's root.</summary>
[TestFixture]
public class ModelPartPathTests
{
    [Test]
    public void EveryPart_IsFoundByItsPath()
    {
        var model = new Entity(null, "Truck");
        var frame = new Entity(model, "Frame");
        var first = new Entity(frame, "Wheel");
        var other = new Entity(frame, "Axle");
        var second = new Entity(frame, "Wheel");
        var odd = new Entity(model, @"Cab/Door [left]\");

        Assert.Multiple(() =>
        {
            Assert.That(ModelPartPath.Of(model, model), Is.EqualTo(""));
            Assert.That(ModelPartPath.Of(model, first), Is.EqualTo("Frame/Wheel"));
            Assert.That(ModelPartPath.Of(model, second), Is.EqualTo("Frame/Wheel[1]"));
            foreach (var part in new[] { model, frame, first, other, second, odd })
            {
                Assert.That(ModelPartPath.Find(model, ModelPartPath.Of(model, part)), Is.SameAs(part), part.Name);
            }
        });
    }

    // A file's unnamed nodes: one must not take the root's path, "", nor its parent's.
    [Test]
    public void PartsWithNoName_HaveAPathOfTheirOwn()
    {
        var model = new Entity(null, "Truck");
        var unnamed = new Entity(model, "");
        var nested = new Entity(unnamed, null);
        var other = new Entity(unnamed, "");

        Assert.Multiple(() =>
        {
            Assert.That(ModelPartPath.Of(model, unnamed), Is.EqualTo("[0]"));
            Assert.That(ModelPartPath.Of(model, nested), Is.EqualTo("[0]/[0]"));
            Assert.That(ModelPartPath.Of(model, other), Is.EqualTo("[0]/[1]"));
            foreach (var part in new[] { unnamed, nested, other })
            {
                Assert.That(ModelPartPath.Find(model, ModelPartPath.Of(model, part)), Is.SameAs(part));
            }

            Assert.That(ModelPartPath.Find(model, "[0]/"), Is.Null, "a trailing separator names no part");
        });
    }

    [Test]
    public void APathOfNoPart_FindsNothing()
    {
        var model = new Entity(null, "Truck");
        _ = new Entity(model, "Wheel");
        var stranger = new Entity(null, "Stranger");

        Assert.Multiple(() =>
        {
            Assert.That(ModelPartPath.Find(model, "Wheel[1]"), Is.Null);
            Assert.That(ModelPartPath.Find(model, "Cab"), Is.Null);
            Assert.That(ModelPartPath.Find(model, "Wheel[x]"), Is.Null);
            Assert.That(ModelPartPath.Of(model, stranger), Is.Null);
        });
    }
}
