using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A ray through a pixel stays a ray with a far plane all but at infinity: unprojected there, a point went to
/// infinity at some turns of the view, and the ray's direction - infinity less infinity - came out NaN; the wheel then
/// moved the editor's camera to NaN (the owner's case, 16.11).</summary>
[TestFixture]
public class PickRayFarPlaneTests
{
    [Test]
    public void TheOwnersView_GivesARay()
    {
        var camera = new Camera(45, 1684, 824, 0.1f, 1000000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Rotation = new QuaternionF(-0.3769597f, -0.085027866f, -0.034776065f, -0.9216625f);
        camera.Update(new AppTime());

        var ray = PickRay.FromCamera(camera, new Vector2F(1335, 509.50003f)).Ray;

        Assert.Multiple(() =>
        {
            Assert.That(ray.Direction.Length(), Is.EqualTo(1).Within(1e-3f), ray.Direction.ToString());
            Assert.That(Vector3F.Dot(ray.Direction, camera.Forward.As<float>()), Is.GreaterThan(0), "ahead, not back");
        });
    }

    // Every pixel of the view, at turns all round, in either projection: a direction, of unit length, ahead of the
    // camera.
    [TestCase(CameraProjectionType.Perspective)]
    [TestCase(CameraProjectionType.Ortho)]
    public void EveryPixel_AtEveryTurn_GivesARayAhead(CameraProjectionType projection)
    {
        var camera = new Camera(45, 1684, 824, 0.1f, 1000000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.ShowProjectionAtOnce(projection, new Vector2F(20, 20));
        for (int yaw = 0; yaw < 360; yaw += 15)
        {
            for (int pitch = -80; pitch <= 80; pitch += 10)
            {
                camera.Rotation = QuaternionF.Multiply(
                    Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(pitch)),
                    Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(yaw)));
                camera.Update(new AppTime());
                for (int x = 0; x <= 1684; x += 421)
                {
                    for (float y = 0; y <= 824; y += 103.5f)
                    {
                        var direction = PickRay.FromCamera(camera, new Vector2F(x, y)).Ray.Direction;
                        var length = direction.Length();
                        if (!float.IsFinite(length) || System.Math.Abs(length - 1) > 1e-3f
                            || Vector3F.Dot(direction, camera.Forward.As<float>()) <= 0)
                        {
                            Assert.Fail($"yaw {yaw} pitch {pitch} pixel {x},{y}: direction {direction}");
                        }
                    }
                }
            }
        }
    }
}
