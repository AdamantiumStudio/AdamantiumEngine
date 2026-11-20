using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class WorldPositionTests
{
    [Test]
    public void APosition_MovesByAVector_AndTwoDifferByOne()
    {
        var start = new WorldPosition(1e9, -2e9, 3);
        var moved = start + new Vector3F(0.25f, -0.5f, 1);

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(1e9 + 0.25));
            Assert.That(moved.Y, Is.EqualTo(-2e9 - 0.5));
            Assert.That(moved.Z, Is.EqualTo(4));
            Assert.That(moved - start, Is.EqualTo(new Vector3<double>(0.25, -0.5, 1)));
            Assert.That(moved - new Vector3F(0.25f, -0.5f, 1), Is.EqualTo(start));
            Assert.That(start + new Vector3<double>(1, 1, 1) - new Vector3<double>(1, 1, 1), Is.EqualTo(start));
        });
    }

    // Far from the origin the way between two near points keeps what float can hold, as a frame's own numbers.
    [Test]
    public void TheOffsetFromANearOrigin_IsExactFarAway()
    {
        var origin = new WorldPosition(1e9, 1e9, 1e9);
        var point = origin + new Vector3F(0.001f, -0.002f, 0.003f);

        Assert.That((point.OffsetFrom(origin) - new Vector3F(0.001f, -0.002f, 0.003f)).Length(), Is.LessThan(1e-6f));
    }

    [Test]
    public void Lerp_GoesTheShareOfTheWay()
    {
        var from = new WorldPosition(1e9, 0, 0);
        var to = new WorldPosition(1e9 + 10, 4, -2);

        Assert.That(WorldPosition.Lerp(from, to, 0.25), Is.EqualTo(new WorldPosition(1e9 + 2.5, 1, -0.5)));
    }

    [Test]
    public void Positions_AreEqualByTheirPlace_AndFiniteOnlyWhenEveryCoordinateIs()
    {
        var position = new WorldPosition(new Vector3<double>(1, 2, 3));

        Assert.Multiple(() =>
        {
            Assert.That(position, Is.EqualTo(new WorldPosition(1, 2, 3)));
            Assert.That(position == new WorldPosition(new Vector3F(1, 2, 3)), Is.True);
            Assert.That(position != WorldPosition.Zero, Is.True);
            Assert.That(position.ToVector(), Is.EqualTo(new Vector3<double>(1, 2, 3)));
            Assert.That(position.IsFinite, Is.True);
            Assert.That(new WorldPosition(1, double.NaN, 3).IsFinite, Is.False);
            Assert.That(new WorldPosition(double.PositiveInfinity, 0, 0).IsFinite, Is.False);
            var nowhere = new WorldPosition(double.NaN, 0, 0);
            var nowhereAgain = new WorldPosition(double.NaN, 0, 0);
            Assert.That(nowhere.Equals(nowhereAgain), Is.True, "a key a dictionary finds again");
            Assert.That(nowhere == nowhereAgain, Is.False, "as a NaN is to a NaN");
        });
    }

    // As a field's meters are typed: one at a time, the others kept.
    [Test]
    public void AMeterSet_ChangesOnlyItsAxis()
    {
        var position = new WorldPosition(1e9, 2, 3);

        position.Y = -7.5;

        Assert.That(position, Is.EqualTo(new WorldPosition(1e9, -7.5, 3)));
    }
}
