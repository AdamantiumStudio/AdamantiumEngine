using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Adamantium.ECS.Components;
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
        var patrol = new SceneGameComponent { On = Robot, Type = "Game.Patrol" };
        patrol.Values.Add(new SceneValue("Speed", "2"));
        patrol.Values.Add(new SceneValue("Route", "A, B, C"));
        scene.GameComponents.Add(patrol);
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

    // A see-through shape keeps how its alpha counts; an opaque one, the default, writes nothing of it.
    [Test]
    public void AShapesAlphaMode_IsReadBack_AndAnOpaqueOneWritesNone()
    {
        var scene = Sample();
        var glass = new SceneShape
        {
            Uid = 51, Name = "Glass", Type = "Adamantium.ECS.Components.SphereShape",
            Color = new Vector4F(0.6f, 0.8f, 1, 0.3f), AlphaMode = AlphaMode.Blend
        };
        scene.Shapes.Add(glass);
        scene.Shapes.Add(new SceneShape
        {
            Uid = 52, Name = "Masked", Type = "Adamantium.ECS.Components.CubeShape", AlphaMode = AlphaMode.Mask,
            AlphaCutoff = 0.8f, Faces = MaterialFaces.Front
        });

        var back = WriteAndRead(scene);

        Assert.Multiple(() =>
        {
            Assert.That(back.Shapes[2].Faces, Is.EqualTo(MaterialFaces.Front));
            Assert.That(back.Shapes[1].Faces, Is.EqualTo(MaterialFaces.Both), "both, the default, not written");
            Assert.That(Text(Sample()), Does.Not.Contain("Faces"));
            Assert.That((back.Shapes[2].AlphaMode, back.Shapes[2].AlphaCutoff), Is.EqualTo((AlphaMode.Mask, 0.8f)));
            Assert.That(back.Shapes[1].AlphaCutoff, Is.EqualTo(0.5f), "the default cutoff, not written");
            Assert.That(back.Shapes[1].AlphaMode, Is.EqualTo(AlphaMode.Blend));
            Assert.That(back.Shapes[1].Color, Is.EqualTo(new Vector4F(0.6f, 0.8f, 1, 0.3f)));
            Assert.That(back.Shapes[1].Values, Is.Empty, "AlphaMode is the entry's own, not a parameter of the shape");
            Assert.That(back.Shapes[0].AlphaMode, Is.EqualTo(AlphaMode.Opaque));
            Assert.That(Text(scene), Does.Contain("AlphaMode=\"Blend\""));
            Assert.That(Text(Sample()), Does.Not.Contain("AlphaMode"));
            Assert.That(Text(Sample()), Does.Not.Contain("AlphaCutoff"));
        });
    }

    // A disabled entry is written so and read back; an enabled one, the default, writes nothing of it.
    [Test]
    public void ADisabledEntry_IsReadBack_AndAnEnabledOneWritesNone()
    {
        var scene = Sample();
        scene.Shapes[0].IsEnabled = false;
        scene.Cameras[0].IsEnabled = false;

        var back = WriteAndRead(scene);

        Assert.Multiple(() =>
        {
            Assert.That(back.Shapes[0].IsEnabled, Is.False);
            Assert.That(back.Cameras[0].IsEnabled, Is.False);
            Assert.That(back.Models[0].IsEnabled, Is.True);
            Assert.That(Text(scene), Does.Contain("Enabled=\"false\""));
            Assert.That(Text(Sample()), Does.Not.Contain("Enabled"));
        });
    }

    // A body writes only what is not a new body's own, a collider its kind by name; an entry without them writes neither.
    [Test]
    public void ABodyAndACollider_AreReadBack_WritingOnlyWhatIsNotByDefault()
    {
        var scene = Sample();
        scene.Shapes[0].Body = new SceneBody { Kind = Adamantium.DarkMatter.BodyKind.Dynamic, Mass = 2.5f, Friction = 0.2f };
        scene.Shapes[0].Collider = new SceneCollider { Kind = ColliderKind.Sphere };
        scene.Markers[0].Body = new SceneBody { Kind = Adamantium.DarkMatter.BodyKind.Static };
        scene.Markers[0].Collider = new SceneCollider { Kind = ColliderKind.None };

        var back = WriteAndRead(scene);
        var text = Text(scene);

        Assert.Multiple(() =>
        {
            Assert.That(back.Shapes[0].Body.Mass, Is.EqualTo(2.5f));
            Assert.That(back.Shapes[0].Body.Friction, Is.EqualTo(0.2f));
            Assert.That(back.Shapes[0].Body.Density, Is.EqualTo(new RigidBody().Density), "not written, read as a new body's");
            Assert.That(back.Shapes[0].Collider.Kind, Is.EqualTo(ColliderKind.Sphere));
            Assert.That(back.Markers[0].Body.Kind, Is.EqualTo(Adamantium.DarkMatter.BodyKind.Static));
            Assert.That(back.Markers[0].Collider.Kind, Is.EqualTo(ColliderKind.None));
            Assert.That(back.Models[0].Body, Is.Null);
            Assert.That(back.Models[0].Collider, Is.Null);
            Assert.That(text, Does.Contain("<Body Mass=\"2.5\" Friction=\"0.2\" />"), "Dynamic and the rest by default");
            Assert.That(text, Does.Contain("<Collider Kind=\"Sphere\" />"));
            Assert.That(text, Does.Contain("<Collider Kind=\"None\" />"));
            Assert.That(Text(Sample()), Does.Not.Contain("<Body").And.Not.Contain("<Collider"));
        });
    }

    // A kind this engine does not know leaves that entry's body or collider out, not the whole scene.
    [Test]
    public void AKindNotKnown_LeavesOnlyThatOut()
    {
        var text = Text(Sample()).Replace("Name=\"Floor\"", "Name=\"Floor\"><Body Kind=\"Floating\" /><Collider Kind=\"Mesh\" /></Shape><Shape Uid=\"99\" Name=\"Spare\"");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var back = SceneFile.Read(stream);

        Assert.Multiple(() =>
        {
            var floor = back.Shapes.First(shape => shape.Name == "Floor");
            Assert.That(floor.Body?.Kind, Is.Null);
            Assert.That(floor.Body?.ToEdits(), Is.Null);
            Assert.That(floor.Collider?.Kind, Is.Null);
            Assert.That(Text(back), Does.Contain("Kind=\"Floating\"").And.Contain("Kind=\"Mesh\""), "written back as read");
        });
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

            var patrol = read.GameComponents[0];
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
              <GameComponents>
                <GameComponent On="00000000000000000000000000000001" Type="SomeMod.Unknown" Glow="0, 1, 0" Mode="Pulse" />
              </GameComponents>
            </Scene>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var again = WriteAndRead(SceneFile.Read(stream));

        Assert.That(again.GameComponents[0].Values,
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
            Assert.That(text, Does.Not.Contain("<Lights").And.Not.Contain("<GameComponents"));
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
