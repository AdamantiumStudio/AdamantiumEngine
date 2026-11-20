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
        subject.Transform.Position = new WorldPosition(30, -5, 12);
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

    // A model's root holds no collider of its own: the orbit goes round the middle of its parts all the same.
    [TestCaseSource(nameof(Orbits))]
    public void AnOrbit_GoesRoundTheMiddleOfTheSubjectsParts(CameraType orbit)
    {
        var subject = new Entity(null, "Model");
        subject.Transform.Position = new WorldPosition(10, 0, 0);
        foreach (var x in new[] { 4.0, 6.0 })
        {
            var part = new Entity(subject, "Part");
            part.Transform.Position = new WorldPosition(x, 0, 0);
            part.AddComponent(new BoxCollider { Size = new Vector3F(1) });
        }

        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        new Entity(subject, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, orbit, distanceToObject: 20);
        camera.RotateRelativeXY(0.3f, 0.5f);
        camera.Update(new AppTime());

        var toMiddle = new WorldPosition(15, 0, 0) - camera.WorldPosition;
        Assert.That(toMiddle.Length(), Is.EqualTo(20).Within(1e-3), "it stands its distance from the middle");
        Assert.That(Vector3<double>.Dot(Vector3<double>.Normalize(toMiddle), camera.Forward), Is.GreaterThan(0.9999),
            "and looks at it");
    }

    // The camera hangs under its subject; what it carries itself - an icon's collider - is no part of what it orbits.
    [Test]
    public void TheCamerasOwnEntity_IsNoPartOfTheSubject()
    {
        var subject = new Entity(null, "Crate");
        AddBox(subject, Vector3<double>.Zero);
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        var cameraEntity = new Entity(subject, "Camera");
        cameraEntity.AddComponent(camera);
        AddBox(cameraEntity, new Vector3<double>(50, 0, 0));
        camera.Update(new AppTime());
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, CameraType.ThirdPersonFree, distanceToObject: 20);
        camera.Update(new AppTime());

        Assert.That((camera.FindSubjectCenter() - WorldPosition.Zero).Length(), Is.LessThan(1e-6));
        Assert.That((camera.WorldPosition - WorldPosition.Zero).Length(), Is.EqualTo(20).Within(1e-3));
        Assert.That(FramedDistance(withIcon: true), Is.EqualTo(FramedDistance(withIcon: false)).Within(1e-6),
            "framed by the crate's size alone");
    }

    private static double FramedDistance(bool withIcon)
    {
        var subject = new Entity(null, "Crate");
        AddBox(subject, Vector3<double>.Zero);
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        var cameraEntity = new Entity(subject, "Camera");
        cameraEntity.AddComponent(camera);
        if (withIcon)
        {
            AddBox(cameraEntity, new Vector3<double>(50, 0, 0));
        }

        camera.Update(new AppTime());
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, CameraType.ThirdPersonFree);
        return camera.Radius;
    }

    // An orbit's middle is measured as it starts; a model rebuilt meanwhile is measured again when the orbit is entered
    // again.
    [Test]
    public void AnOrbitEnteredAgain_MeasuresTheSubjectAgain()
    {
        var subject = new Entity(null, "Model");
        var part = new Entity(subject, "Part");
        AddBox(part, Vector3<double>.Zero);
        _ = new Entity(subject, "Other part");
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        new Entity(subject, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, CameraType.ThirdPersonFree, distanceToObject: 20);
        camera.Update(new AppTime());

        part.Transform.Position = new WorldPosition(8, 0, 0);
        var keptWhileOrbiting = camera.FindSubjectCenter();
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, CameraType.ThirdPersonFree, distanceToObject: 20);

        Assert.That((keptWhileOrbiting - WorldPosition.Zero).Length(), Is.LessThan(1e-6), "the pivot holds while the orbit lasts");
        Assert.That((camera.FindSubjectCenter() - new WorldPosition(8, 0, 0)).Length(), Is.LessThan(1e-6));
    }

    private static void AddBox(Entity entity, Vector3<double> center)
    {
        entity.AddComponent(new BoxCollider { Size = new Vector3F(1), Position = center.As<float>() });
    }
}
