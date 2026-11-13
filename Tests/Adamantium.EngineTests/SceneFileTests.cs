using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core.Models;
using CameraProjectionType = Adamantium.ECS.Components.CameraProjectionType;
using CameraType = Adamantium.Graphics.Core.CameraType;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A scene file read back is the scene written: every section, the ids that tie entries together, the values
/// of components whose code is not loaded; a file of a later version is refused.</summary>
[TestFixture]
public class SceneFileTests
{
    private static readonly UInt128 Hangar = UInt128.Parse("7f3a0000000000000000000000000002", NumberStyles.HexNumber);
    private static readonly UInt128 Robot = new(0x1234_5678_9abc_def0, 0x0fed_cba9_8765_4321);

    private static SceneFile Sample()
    {
        var scene = new SceneFile();
        scene.Models.Add(new SceneModel
        {
            Uid = Robot, Name = "Robot", Asset = "Models/Robot/robot.glb",
            Position = new Vector3<double>(4, 0, -2.5), Rotation = new QuaternionF(0, 0.7071f, 0, 0.7071f),
            Scale = new Vector3F(2, 2, 2)
        });
        scene.Models.Add(new SceneModel { Uid = Hangar, Name = "Hangar", Asset = "Models/Hangar/hangar.fbx" });
        scene.Lights.Add(new SceneLight
        {
            Uid = 10, Name = "Hangar lamp", Type = LightType.Spot, Parent = Hangar,
            Position = new Vector3<double>(0, 6, 0), Color = new Vector3F(1, 0.9f, 0.8f), Intensity = 3, Range = 15,
            InnerSpotAngle = 20, OuterSpotAngle = 35, Shadows = true, ShadowMapResolution = 2048
        });
        scene.Cameras.Add(new SceneCamera
        {
            Uid = 20, Name = "Map", Projection = CameraProjectionType.Ortho, OrthoScale = new Vector2F(40, 30),
            Near = 1, Far = 500, Type = CameraType.ThirdPersonLocked, Parent = Robot
        });
        scene.Markers.Add(new SceneMarker { Uid = 40, Name = "Props", Kind = SceneMarkerKind.Group });
        var floor = new SceneShape
        {
            Uid = 50, Name = "Floor", Type = "Adamantium.ECS.Components.CubeShape", Color = new Vector4F(0.2f, 0.4f, 0.6f, 1),
            Scale = new Vector3F(20, 1, 20)
        };
        floor.Values.Add(new SceneValue("Size", "1, 0.1, 1"));
        scene.Shapes.Add(floor);
        scene.SpawnPoints.Add(new SceneSpawnPoint
            { Uid = 30, Name = "Player start", Position = new Vector3<double>(0, 0, 5) });
        var patrol = new SceneBehaviour { On = Robot, Type = "Game.Patrol" };
        patrol.Values.Add(new SceneValue("Speed", "2"));
        patrol.Values.Add(new SceneValue("Route", "A, B, C"));
        scene.Behaviours.Add(patrol);
        return scene;
    }

    private static SceneFile WriteAndRead(SceneFile scene)
    {
        using var stream = new MemoryStream();
        scene.Write(stream);
        stream.Position = 0;
        return SceneFile.Read(stream);
    }

    private static string Text(SceneFile scene)
    {
        using var stream = new MemoryStream();
        scene.Write(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // The scene keeps its own id across saving; a new scene's file has none until its scene is saved.
    [Test]
    public void TheScenesId_IsReadBack_AndAFileWithoutOneHasNone()
    {
        var scene = Sample();
        scene.Uid = Robot;

        Assert.Multiple(() =>
        {
            Assert.That(WriteAndRead(scene).Uid, Is.EqualTo(Robot));
            Assert.That(Text(scene), Does.Contain("<Scene Version=\"1\" Uid=\"123456789abcdef00fedcba987654321\""));
            Assert.That(WriteAndRead(new SceneFile()).Uid, Is.Null);
            Assert.That(Text(new SceneFile()), Does.Not.Contain("Uid="));
        });
    }

    // The editor's section comes back as it was written, whatever is in it; a file without one has none.
    [Test]
    public void TheEditorsSection_IsReadBackAsWritten_AndAFileWithoutOneHasNone()
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml("<Editor><View Name=\"Top\" Position=\"0, -30, 0\" /><Selected Uid=\"1\" /></Editor>");
        var scene = Sample();
        scene.Editor = document.DocumentElement;

        var back = WriteAndRead(scene);

        Assert.Multiple(() =>
        {
            Assert.That(back.Editor?.OuterXml, Is.EqualTo(document.DocumentElement.OuterXml));
            Assert.That(Text(back), Is.EqualTo(Text(scene)), "saved again, the file is the same");
            Assert.That(back.Models, Has.Count.EqualTo(scene.Models.Count), "the scene itself unchanged");
            Assert.That(WriteAndRead(Sample()).Editor, Is.Null);
            Assert.That(Text(Sample()), Does.Not.Contain("<Editor"));
        });
    }

    [Test]
    public void AScene_ReadBack_IsTheSceneWritten()
    {
        var read = WriteAndRead(Sample());

        Assert.Multiple(() =>
        {
            var robot = read.Models[0];
            Assert.That(robot.Uid, Is.EqualTo(Robot));
            Assert.That(robot.Name, Is.EqualTo("Robot"));
            Assert.That(robot.Asset, Is.EqualTo("Models/Robot/robot.glb"));
            Assert.That(robot.Position, Is.EqualTo(new Vector3<double>(4, 0, -2.5)));
            Assert.That(robot.Rotation, Is.EqualTo(new QuaternionF(0, 0.7071f, 0, 0.7071f)));
            Assert.That(robot.Scale, Is.EqualTo(new Vector3F(2, 2, 2)));
            Assert.That(robot.Parent, Is.Null);

            var hangar = read.Models[1];
            Assert.That(hangar.Rotation, Is.EqualTo(QuaternionF.Identity));
            Assert.That(hangar.Scale, Is.EqualTo(Vector3F.One));

            var lamp = read.Lights[0];
            Assert.That(lamp.Parent, Is.EqualTo(Hangar));
            Assert.That(lamp.Type, Is.EqualTo(LightType.Spot));
            Assert.That(lamp.Color, Is.EqualTo(new Vector3F(1, 0.9f, 0.8f)));
            Assert.That((lamp.Intensity, lamp.Range, lamp.InnerSpotAngle, lamp.OuterSpotAngle),
                Is.EqualTo((3f, 15f, 20f, 35f)));
            Assert.That((lamp.Shadows, lamp.ShadowMapResolution), Is.EqualTo((true, 2048)));

            var map = read.Cameras[0];
            Assert.That(map.Projection, Is.EqualTo(CameraProjectionType.Ortho));
            Assert.That(map.OrthoScale, Is.EqualTo(new Vector2F(40, 30)));
            Assert.That((map.Near, map.Far), Is.EqualTo((1f, 500f)));
            Assert.That((map.Type, map.Parent), Is.EqualTo((CameraType.ThirdPersonLocked, (UInt128?)Robot)));

            Assert.That((read.Markers[0].Uid, read.Markers[0].Kind), Is.EqualTo(((UInt128)40, SceneMarkerKind.Group)));
            var floor = read.Shapes[0];
            Assert.That(floor.Type, Is.EqualTo("Adamantium.ECS.Components.CubeShape"));
            Assert.That(floor.Color, Is.EqualTo(new Vector4F(0.2f, 0.4f, 0.6f, 1)));
            Assert.That(floor.Scale, Is.EqualTo(new Vector3F(20, 1, 20)));
            Assert.That(floor.Values, Is.EqualTo(new[] { new SceneValue("Size", "1, 0.1, 1") }));

            Assert.That(read.SpawnPoints[0].Position, Is.EqualTo(new Vector3<double>(0, 0, 5)));

            var patrol = read.Behaviours[0];
            Assert.That(patrol.On, Is.EqualTo(Robot));
            Assert.That(patrol.Type, Is.EqualTo("Game.Patrol"));
            Assert.That(patrol.Values,
                Is.EqualTo(new[] { new SceneValue("Speed", "2"), new SceneValue("Route", "A, B, C") }));
        });
    }

    [Test]
    public void AComponentWhoseCodeIsNotLoaded_IsWrittenBackAsItWasRead()
    {
        const string text = """
            <Scene Version="1">
              <Behaviours>
                <Behaviour On="00000000000000000000000000000001" Type="SomeMod.Unknown" Glow="0, 1, 0" Mode="Pulse" />
              </Behaviours>
            </Scene>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var again = WriteAndRead(SceneFile.Read(stream));

        Assert.That(again.Behaviours[0].Values,
            Is.EqualTo(new[] { new SceneValue("Glow", "0, 1, 0"), new SceneValue("Mode", "Pulse") }));
    }

    [Test]
    public void TheFile_IsWrittenInTheInvariantCulture_WhateverTheCurrentOne()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            var text = Text(Sample());

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Position=\"4, 0, -2.5\""));
                Assert.That(WriteAndRead(Sample()).Models[0].Position, Is.EqualTo(new Vector3<double>(4, 0, -2.5)));
            });
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Test]
    public void WhatIsAsItIsByDefault_AndEmptySections_AreLeftOut()
    {
        var scene = new SceneFile();
        scene.Models.Add(new SceneModel { Uid = Hangar, Name = "Hangar", Asset = "Models/Hangar/hangar.fbx" });

        var text = Text(scene);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("Rotation=").And.Not.Contain("Scale=").And.Not.Contain("Parent="));
            Assert.That(text, Does.Not.Contain("<Lights").And.Not.Contain("<Behaviours"));
            Assert.That(text, Does.Not.Contain("xmlns"));
        });
    }

    [Test]
    public void AFileOfALaterVersion_IsRefused()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<Scene Version=\"{SceneFile.FormatVersion + 1}\" />"));

        var refusal = Assert.Throws<InvalidDataException>(() => SceneFile.Read(stream));
        Assert.That(refusal.Message, Does.Contain("version"));
    }

    [Test]
    public void SomethingElse_IsRefusedAsNoSceneFile()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<Project Sdk=\"Microsoft.NET.Sdk\" />"));

        Assert.Throws<InvalidDataException>(() => SceneFile.Read(stream));
    }

    [Test]
    public void WritingToAPath_LeavesTheSceneAndNoWorkingFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SceneFileTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "Level1" + SceneFile.FileExtension);

            Sample().Write(path);

            Assert.Multiple(() =>
            {
                Assert.That(SceneFile.Read(path).Models, Has.Count.EqualTo(2));
                Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
