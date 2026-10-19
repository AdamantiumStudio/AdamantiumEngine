using System;
using System.Linq;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A model whose joints and moving parts are its own nodes, as glTF has them: the skin bends with the joints'
/// entities, and a clip poses those entities - a joint or a wheel alike.</summary>
[TestFixture]
public class SkinAndClipTests
{
    // An entity the import gave a pivot of its own still takes a node's pose about its origin.
    [Test]
    public void APose_StandsTheNodeAsTheFileHasIt_WhateverItsPivot()
    {
        var entity = new Entity(null, "Wheel");
        entity.Transform.Position = new Vector3(1, 0, 0);
        entity.Transform.Pivot = new Vector3(3, 2, 1);
        var turn = QuaternionF.RotationAxis(Vector3F.UnitY, 0.7f);

        entity.Transform.SetPose(new Vector3(5, 6, 7), turn, new Vector3F(2, 1, 1));

        var expected = Matrix4x4F.Scaling(2, 1, 1) * Matrix4x4F.RotationQuaternion(turn) * Matrix4x4F.Translation(5, 6, 7);
        Assert.That(entity.Transform.GetLocalMatrixF(), Is.EqualTo(expected).Using<Matrix4x4F>(Near));
    }

    [Test]
    public void ASkinnedNode_GetsASkin_OfItsJointEntities()
    {
        var model = Import(SkinnedScene(), out var leg, out var hip, out var knee);

        var skin = leg.GetComponent<Skin>();

        Assert.Multiple(() =>
        {
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.Joints, Is.EqualTo(new[] { hip, knee }));
            Assert.That(model.GetComponent<AnimationController>(), Is.Null, "no skeleton of its own");
        });
    }

    [Test]
    public void AtTheBindPose_TheSkinLeavesTheMeshAsItIs_AndAMovedJointCarriesItsPart()
    {
        Import(SkinnedScene(), out var leg, out _, out var knee);
        var skin = leg.GetComponent<Skin>();
        var palette = new Matrix4x4F[4];

        skin.FillPalette(palette);
        var bound = palette.Take(2).ToArray();
        knee.Transform.Position += new Vector3(1, 0, 0);
        skin.FillPalette(palette);

        Assert.Multiple(() =>
        {
            Assert.That(bound, Has.All.EqualTo(Matrix4x4F.Identity).Using<Matrix4x4F>(Near));
            Assert.That(palette[0], Is.EqualTo(Matrix4x4F.Identity).Using<Matrix4x4F>(Near), "the hip stayed");
            Assert.That(palette[1].TranslationVector, Is.EqualTo(new Vector3F(1, 0, 0)).Using<Vector3F>(Near),
                "the knee's vertices go where it went");
        });
    }

    // A turn does not commute with a move, and the mesh's own node stands away from the origin, turned: the vertex still
    // lands where its joint carries it, whatever the node holding the mesh does.
    [Test]
    public void AVertex_GoesWhereItsJointCarriesIt_HoweverTheMeshsNodeStands()
    {
        Import(SkinnedScene(), out var leg, out _, out var knee);
        leg.Transform.Position = new Vector3(5, 0, 0);
        leg.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, 0.5f);
        knee.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitZ, MathF.PI / 2);
        var palette = new Matrix4x4F[4];

        leg.GetComponent<Skin>().FillPalette(palette);
        var drawn = Vector3F.TransformCoordinate(new Vector3F(1, -1, 0), palette[1] * leg.Transform.GetWorldMatrixF());

        // One unit along X from the knee when bound; the knee's quarter turn carries it round the knee.
        var expected = Vector3F.TransformCoordinate(Vector3F.UnitX, Matrix4x4F.RotationZ(MathF.PI / 2)) + new Vector3F(0, -1, 0);
        Assert.That(drawn, Is.EqualTo(expected).Using<Vector3F>(Near));
    }

    [Test]
    public void AnotherClip_PutsBackWhatTheLastOneMoved()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(2, -1, 0)));
        scene.Clips["Wave"] = Clip("hip", (0, Vector3F.Zero), (1, new Vector3F(0, 0, 3)));
        var model = Import(scene, out _, out var hip, out var knee);
        var animation = model.GetComponent<AnimationComponent>();

        animation.Pose(1);
        var bent = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.CurrentClip = "Wave";
        animation.Pose(1);

        Assert.Multiple(() =>
        {
            Assert.That(bent, Is.EqualTo(new Vector3F(2, -1, 0)).Using<Vector3F>(Near));
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(0, -1, 0)).Using<Vector3F>(Near),
                "the knee, which Wave does not move, is back at rest");
            Assert.That(hip.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(0, 0, 3)).Using<Vector3F>(Near));
        });
    }

    [TestCase(null)]
    [TestCase("No such clip")]
    public void NoClip_StopsThePlay_AndTheNextPoseStandsTheNodesAtRest(string clip)
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(2, -1, 0)));
        var model = Import(scene, out _, out _, out var knee);
        var animation = model.GetComponent<AnimationComponent>();
        animation.Pose(1);

        animation.CurrentClip = clip;
        animation.Pose(0);

        Assert.Multiple(() =>
        {
            Assert.That(animation.Animations, Is.Empty);
            Assert.That(animation.Duration, Is.Zero);
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(0, -1, 0)).Using<Vector3F>(Near));
        });
    }

    // A clip one second long, played as each option says.
    [TestCase(CalculateOptions.CyclicForward, 1.25, 0.25)]
    [TestCase(CalculateOptions.CyclicBackward, 0.25, 0.75)]
    [TestCase(CalculateOptions.CyclicForwardBackward, 1.25, 0.75)]
    [TestCase(CalculateOptions.CyclicForwardBackward, 2.25, 0.25)]
    [TestCase(CalculateOptions.CyclicBackwardForward, 1.25, 0.25)]
    [TestCase(CalculateOptions.OneTimeForward, 3, 1)]
    [TestCase(CalculateOptions.OneTimeBackward, 3, 0)]
    [TestCase(CalculateOptions.OneTimeForwardBackward, 1.5, 0.5)]
    [TestCase(CalculateOptions.OneTimeForwardBackward, 3, 0)]
    public void EachOption_PlaysTheClipItsWay(CalculateOptions option, double elapsed, double time)
    {
        Assert.That(AnimationComponent.TimeAt(elapsed, 1, option), Is.EqualTo(time).Within(1e-9));
    }

    [Test]
    public void NoAnimation_PosesNothing()
    {
        Assert.That(AnimationComponent.TimeAt(0.5, 1, CalculateOptions.NoAnimation), Is.Null);
    }

    [Test]
    public void EveryClip_IsPlayable_ByName_AndPosesItsNodes()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(2, -1, 0)));
        scene.Clips["Kick"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(0, -1, 4)));
        var model = Import(scene, out _, out _, out var knee);
        var animation = model.GetComponent<AnimationComponent>();

        animation.Pose(0.5);
        var halfwayBending = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.CurrentClip = "Kick";
        animation.Pose(1);
        var kicked = knee.Transform.GetLocalMatrixF().TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(animation.ClipNames, Is.EquivalentTo(new[] { "Bend", "Kick" }));
            Assert.That(animation.Targets["knee"], Is.SameAs(knee));
            Assert.That(halfwayBending, Is.EqualTo(new Vector3F(1, -1, 0)).Using<Vector3F>(Near), "blended halfway");
            Assert.That(kicked, Is.EqualTo(new Vector3F(0, -1, 4)).Using<Vector3F>(Near), "the clip chosen plays");
            Assert.That(animation.Duration, Is.EqualTo(1));
        });
    }

    [Test]
    public void ASteppedKey_HoldsUntilTheNext()
    {
        var frames = Clip("node", (0, Vector3F.Zero), (1, Vector3F.One))["node"];
        frames[0].Interpolation = InterpolationType.Step;

        Assert.Multiple(() =>
        {
            Assert.That(AnimationComponent.Sample(frames, 0.9).Position, Is.EqualTo(Vector3F.Zero));
            Assert.That(AnimationComponent.Sample(frames, 1.5).Position, Is.EqualTo(Vector3F.One), "the last held after");
            Assert.That(AnimationComponent.Sample(frames, -1).Position, Is.EqualTo(Vector3F.Zero), "the first held before");
        });
    }

    // A leg mesh at the origin, bound to a hip at the origin and a knee one unit up (-Y), the knee its own node.
    private static SceneData SkinnedScene()
    {
        var scene = new SceneData { Name = "leg" };
        scene.Models = scene.CreateMesh(null, "", "Model");
        var leg = scene.CreateMesh(scene.Models, "leg", "Leg");
        var mesh = new Mesh(PrimitiveType.TriangleList);
        mesh.SetPoints([new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, -1, 0)]).SetIndices([0, 1, 2]);
        mesh.SetJointIndices([new Vector4F(0, 1, 0, 0), new Vector4F(0, 1, 0, 0), new Vector4F(0, 1, 0, 0)]);
        mesh.SetJointWeights([new Vector4F(0.5f, 0.5f, 0, 0), new Vector4F(0.5f, 0.5f, 0, 0), new Vector4F(0.5f, 0.5f, 0, 0)]);
        leg.Meshes.Add(mesh);
        var hip = scene.CreateMesh(scene.Models, "hip", "Hip");
        var knee = scene.CreateMesh(hip, "knee", "Knee");
        knee.Position = new Vector3F(0, -1, 0);
        scene.Controllers["leg"] = new SceneData.Controller
        {
            ControllerId = "leg",
            JointsAreNodes = true,
            JointNames = ["hip", "knee"],
            JointMatrices = [Matrix4x4F.Identity, Matrix4x4F.Translation(0, 1, 0)]
        };
        return scene;
    }

    private static SceneData.AnimationCollection Clip(string node, params (double Time, Vector3F Position)[] keys)
    {
        var frames = new SceneData.FrameCollection { JointId = node };
        foreach (var (time, position) in keys)
        {
            frames.Add(new SceneData.KeyFrame(Vector3F.One, position, QuaternionF.Identity) { TimeStamp = time });
        }

        return new SceneData.AnimationCollection { [node] = frames };
    }

    private static Entity Import(SceneData scene, out Entity leg, out Entity hip, out Entity knee)
    {
        var model = new EntityImportTemplate(scene, null, Vector3.Zero).BuildEntity(new Entity()).Result;
        leg = model.Dependencies.Single(e => e.Name.StartsWith("Leg"));
        hip = model.Dependencies.Single(e => e.Name.StartsWith("Hip"));
        knee = hip.Dependencies.Single();
        return model;
    }

    private static int Near(Matrix4x4F a, Matrix4x4F b)
    {
        for (int i = 0; i < 16; i++)
        {
            if (System.Math.Abs(a[i] - b[i]) > 1e-4f) return 1;
        }

        return 0;
    }

    private static int Near(Vector3F a, Vector3F b) => (a - b).Length() < 1e-4f ? 0 : 1;
}
