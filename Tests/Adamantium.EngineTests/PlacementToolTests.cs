using Adamantium.Engine.Tools;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class PlacementToolTests
{
    [Test]
    public void ARayLookingDown_MeetsTheGroundUnderIt_FarFromTheWorldsOrigin()
    {
        var eye = new Vector3(1e7, -10, 2e7);
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(1, 1, 0)));

        Assert.That(PlacementTool.OnGround(ray, eye, 0, 1000, out var point), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(point.X, Is.EqualTo(1e7 + 10).Within(1e-3));
            Assert.That(point.Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(point.Z, Is.EqualTo(2e7).Within(1e-3));
        });
    }

    [Test]
    public void ARaisedGround_IsMetAtItsHeight()
    {
        var ray = new Ray(Vector3F.Zero, Vector3F.UnitY);

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), -4, 1000, out var point), Is.True);
        Assert.That(point.Y, Is.EqualTo(-4).Within(1e-6));
    }

    [TestCase(0, -1, 0)]
    [TestCase(1, 0, 0)]
    public void ARayAwayFromOrAlongTheGround_MeetsNothing(float x, float y, float z)
    {
        var ray = new Ray(Vector3F.Zero, new Vector3F(x, y, z));

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), 0, 1000, out _), Is.False);
    }

    [Test]
    public void GroundPastTheReach_IsNotMet()
    {
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(100, 1, 0)));

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), 0, 500, out _), Is.False);
    }

    [Test]
    public void AnObject_RestsOnThePointByItsLowestPoint_UpBeingMinusY()
    {
        var position = PlacementTool.RestOn(new Vector3(3, 0, 4), 0.5f);

        Assert.That(position, Is.EqualTo(new Vector3(3, -0.5, 4)));
    }
}
