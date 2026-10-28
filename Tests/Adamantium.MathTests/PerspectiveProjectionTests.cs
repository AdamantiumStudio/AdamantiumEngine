using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class PerspectiveProjectionTests
{
    private const float Near = 0.1f;
    private const float Far = 1000f;

    private static Matrix4x4F FovX()
    {
        return Matrix4x4.CreatePerspectiveFovX<float>(float.DegreesToRadians(60), 4f / 3, Near, Far);
    }

    private static Matrix4x4F FovY()
    {
        return Matrix4x4.CreatePerspectiveFovY<float>(float.DegreesToRadians(60), 3f / 4, Near, Far);
    }

    [Test]
    public void FovX_PointsOnOneRayFromTheEyeLandTogether()
    {
        AssertOneRayLandsTogether(FovX());
    }

    [Test]
    public void FovY_PointsOnOneRayFromTheEyeLandTogether()
    {
        AssertOneRayLandsTogether(FovY());
    }

    [Test]
    public void FovX_SpansItsFovAcross()
    {
        var projection = FovX();
        var halfWidth = 1f / (float)System.Math.Tan(float.DegreesToRadians(30));

        Assert.That(projection.M11, Is.EqualTo(halfWidth).Within(1e-4f));
        Assert.That(projection.M22, Is.EqualTo(halfWidth * 4f / 3).Within(1e-4f));
    }

    [Test]
    public void FovX_MapsTheNearAndFarPlanesToZeroAndOne()
    {
        AssertDepthRange(FovX());
    }

    [Test]
    public void FovY_MapsTheNearAndFarPlanesToZeroAndOne()
    {
        AssertDepthRange(FovY());
    }

    private static void AssertOneRayLandsTogether(Matrix4x4F projection)
    {
        var near = Vector3F.TransformCoordinate(new Vector3F(1, 0.5f, 2), projection);
        var far = Vector3F.TransformCoordinate(new Vector3F(2, 1, 4), projection);

        Assert.That(far.X, Is.EqualTo(near.X).Within(1e-5f));
        Assert.That(far.Y, Is.EqualTo(near.Y).Within(1e-5f));
    }

    private static void AssertDepthRange(Matrix4x4F projection)
    {
        Assert.That(Vector3F.TransformCoordinate(new Vector3F(0, 0, Near), projection).Z, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(Vector3F.TransformCoordinate(new Vector3F(0, 0, Far), projection).Z, Is.EqualTo(1f).Within(1e-5f));
    }
}
