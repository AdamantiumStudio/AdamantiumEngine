using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class CameraModeTests
{
    private static readonly CameraType[] Orbits = [CameraType.ThirdPersonFree, CameraType.ThirdPersonFreeAlt, CameraType.ThirdPersonLocked];

    [TestCaseSource(nameof(Orbits))]
    public void LeavingAnOrbit_KeepsWhereTheCameraStandsAndLooks(CameraType orbit)
    {
        var subject = new Entity(null, "Subject");
        subject.Transform.Position = new Vector3<double>(30, -5, 12);
        subject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(0.7f, 0.2f, 0.1f);
        subject.Transform.ScaleFactor = new Vector3F(0.01f);
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        camera.SetThirdPersonCamera(subject, new Vector3F(0.3f, -0.4f, 0), orbit, distanceToObject: 20);
        camera.RotateRelativeXY(0.3f, 0.5f);
        camera.Update(new AppTime());
        var standing = camera.WorldPosition;
        var forward = camera.Forward;
        var up = camera.Up;

        camera.SetFreeCamera();
        camera.Update(new AppTime());

        Assert.That((camera.WorldPosition - standing).Length(), Is.LessThan(1e-3), $"{camera.WorldPosition} instead of {standing}");
        Assert.That(Vector3<double>.Dot(camera.Forward, forward), Is.GreaterThan(0.9999), $"{camera.Forward} instead of {forward}");
        Assert.That(Vector3<double>.Dot(camera.Up, up), Is.GreaterThan(0.9999), $"{camera.Up} instead of {up}");
    }
}
