using System;
using System.Linq;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates;
using Adamantium.Multiverse;
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

    // A draw takes the palette from the world matrices the transforms were worked out with for its camera this frame -
    // one lookup a joint, for every mesh sharing the joints - and gets what composing every joint's parents gives; for a
    // camera nothing was worked out for yet, it composes them.
    [Test]
    public void APaletteFromTheFramesWorlds_IsThePaletteComposedAfresh()
    {
        var universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        var output = new TestOutput();
        universe.Add(output);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Camera", Vector3.Zero, Vector3.ForwardLH, -Vector3.Up,
            800, 600, 0.1f, 1000f);
        output.Camera = cameraEntity.GetComponent<Camera>();
        var model = Import(SkinnedScene(), out var leg, out _, out var knee);
        model.Transform.Position = new Vector3(2, 0, 5);
        leg.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, 0.5f);
        knee.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitZ, MathF.PI / 2);
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);
        universe.EntityWorld.EntityManager.AddEntity(model);
        universe.EntityWorld.ForceUpdate();
        var transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
        transforms.Update(new AppTime());
        var skin = leg.GetComponent<Skin>();
        var composed = new Matrix4x4F[4];
        var looked = new Matrix4x4F[4];
        var unseen = new Matrix4x4F[4];

        skin.FillPalette(composed);
        skin.FillPalette(looked, output.Camera);
        skin.FillPalette(unseen, new CameraTemplate().BuildEntity(null, "Other", Vector3.Zero, Vector3.ForwardLH,
            -Vector3.Up, 800, 600, 0.1f, 1000f).GetComponent<Camera>());
        knee.Transform.Position += new Vector3(1, 0, 0);
        transforms.Update(new AppTime());
        var movedComposed = new Matrix4x4F[4];
        var movedLooked = new Matrix4x4F[4];
        skin.FillPalette(movedComposed);
        skin.FillPalette(movedLooked, output.Camera);

        Assert.Multiple(() =>
        {
            Assert.That(knee.Transform.GetMetadata(output.Camera).Computed, Is.True, "the frame's worlds were there");
            Assert.That(looked.Take(2), Is.EqualTo(composed.Take(2)).Using<Matrix4x4F>(Near));
            Assert.That(unseen.Take(2), Is.EqualTo(composed.Take(2)).Using<Matrix4x4F>(Near));
            Assert.That(movedLooked.Take(2), Is.EqualTo(movedComposed.Take(2)).Using<Matrix4x4F>(Near));
            Assert.That(movedComposed[1], Is.Not.EqualTo(composed[1]).Using<Matrix4x4F>(Near), "the knee moved");
        });
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

    // A copy bends with its own joints and its clips pose its own nodes; the original is left as it was.
    [Test]
    public void ADuplicate_BendsWithItsOwnJoints_AndPlaysOnItsOwnNodes()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(2, -1, 0)));
        var model = Import(scene, out var leg, out var hip, out var knee);

        var copy = model.Duplicate();
        var copiedLeg = copy.Dependencies.Single(e => e.Name.StartsWith("Leg"));
        var copiedHip = copy.Dependencies.Single(e => e.Name.StartsWith("Hip"));
        var copiedKnee = copiedHip.Dependencies.Single();
        var skin = copiedLeg.GetComponent<Skin>();
        var animation = copy.GetComponent<AnimationComponent>();
        animation.Pose(1);

        Assert.Multiple(() =>
        {
            Assert.That(skin.Joints, Is.EqualTo(new[] { copiedHip, copiedKnee }));
            Assert.That(skin.InverseBindMatrices, Is.EqualTo(scene.Controllers["leg"].JointMatrices));
            Assert.That(leg.GetComponent<Skin>().InverseBindMatrices, Is.EqualTo(scene.Controllers["leg"].JointMatrices),
                "the original's inverse bind matrices stay");
            Assert.That(skin.Skeleton, Is.Not.SameAs(leg.GetComponent<Skin>().Skeleton));
            Assert.That(animation.Targets["knee"], Is.SameAs(copiedKnee));
            Assert.That(animation.RestPoses.Keys, Is.EquivalentTo(model.GetComponent<AnimationComponent>().RestPoses.Keys));
            Assert.That(copiedKnee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(2, -1, 0)).Using<Vector3F>(Near));
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(0, -1, 0)).Using<Vector3F>(Near),
                "the original's knee stays");
            Assert.That(leg.GetComponent<Skin>().Joints, Is.EqualTo(new[] { hip, knee }));
        });
    }

    [Test]
    public void ADuplicate_StandsWhereTheOriginalStands()
    {
        var model = Import(SkinnedScene(), out _, out _, out var knee);
        model.Transform.Position = new Vector3(4, 0, 0);
        knee.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitZ, 0.5f);

        var copy = model.Duplicate();
        var copiedKnee = copy.Dependencies.Single(e => e.Name.StartsWith("Hip")).Dependencies.Single();

        Assert.Multiple(() =>
        {
            Assert.That(copy.Transform.Position, Is.EqualTo(new Vector3(4, 0, 0)));
            Assert.That(copiedKnee.Transform.GetLocalMatrixF(), Is.EqualTo(knee.Transform.GetLocalMatrixF()).Using<Matrix4x4F>(Near));
        });
    }

    // A copy of a model paused mid-clip stands as the original does, and choosing its clips leaves the original's alone.
    [Test]
    public void ADuplicate_OfAPausedModel_KeepsItsPose_AndItsOwnClips()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (1, new Vector3F(2, -1, 0)));
        var model = Import(scene, out _, out _, out _);
        var animation = model.GetComponent<AnimationComponent>();
        animation.Pose(1);
        animation.IsPaused = true;

        var copy = model.Duplicate();
        var copiedAnimation = copy.GetComponent<AnimationComponent>();
        copiedAnimation.Update(new AppTime());
        copiedAnimation.Clips.Remove("Bend");

        Assert.Multiple(() =>
        {
            Assert.That(copy.Dependencies.Single(e => e.Name.StartsWith("Hip")).Dependencies.Single().Transform.GetLocalMatrixF().TranslationVector,
                Is.EqualTo(new Vector3F(2, -1, 0)).Using<Vector3F>(Near));
            Assert.That(animation.ClipNames, Is.EquivalentTo(new[] { "Bend" }));
        });
    }

    // A pause stops the clip's clock: played on, the clip goes from where it was paused, not from where it would have got.
    [Test]
    public void APause_StopsTheClipsClock()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (2, new Vector3F(4, -1, 0)));
        var model = Import(scene, out _, out _, out var knee);
        var animation = model.GetComponent<AnimationComponent>();

        animation.Update(At(0));
        animation.Update(At(0.5));
        animation.IsPaused = true;
        animation.Update(At(0.6));
        animation.Update(At(1.5));
        var paused = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.IsPaused = false;
        animation.Update(At(1.5));
        var resumed = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.Update(At(1.75));

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo(new Vector3F(1, -1, 0)).Using<Vector3F>(Near));
            Assert.That(resumed, Is.EqualTo(new Vector3F(1, -1, 0)).Using<Vector3F>(Near));
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(1.5f, -1, 0)).Using<Vector3F>(Near));
        });
    }

    // A speed set in a pause goes on from the paused pose, at the new speed.
    [Test]
    public void ASpeedSetInAPause_GoesOnFromThePausedPose()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(0, -1, 0)), (2, new Vector3F(4, -1, 0)));
        var model = Import(scene, out _, out _, out var knee);
        var animation = model.GetComponent<AnimationComponent>();

        animation.Update(At(0));
        animation.Update(At(0.5));
        animation.IsPaused = true;
        animation.Update(At(0.6));
        animation.AnimationSpeed = 2;
        animation.IsPaused = false;
        animation.Update(At(1));
        var resumed = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.Update(At(1.25));

        Assert.Multiple(() =>
        {
            Assert.That(resumed, Is.EqualTo(new Vector3F(1, -1, 0)).Using<Vector3F>(Near));
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(2, -1, 0)).Using<Vector3F>(Near));
        });
    }

    // Switched off, an animation stands its nodes at rest once and leaves them be; switched on, it plays from the start.
    [Test]
    public void ASwitchedOffAnimation_StandsAtRest_AndSwitchedOnPlaysFromTheStart()
    {
        var scene = SkinnedScene();
        scene.Clips["Bend"] = Clip("knee", (0, new Vector3F(2, -1, 0)), (2, new Vector3F(6, -1, 0)));
        var model = Import(scene, out _, out _, out var knee);
        var animation = model.GetComponent<AnimationComponent>();

        animation.Update(At(0));
        animation.Update(At(0.5));
        animation.IsEnabled = false;
        animation.Update(At(0.6));
        var off = knee.Transform.GetLocalMatrixF().TranslationVector;
        knee.Transform.Position = new Vector3(7, -1, 0);
        animation.Update(At(0.7));
        var moved = knee.Transform.GetLocalMatrixF().TranslationVector;
        animation.IsEnabled = true;
        animation.Update(At(3));
        animation.Update(At(3.5));

        Assert.Multiple(() =>
        {
            Assert.That(off, Is.EqualTo(new Vector3F(0, -1, 0)).Using<Vector3F>(Near));
            Assert.That(moved, Is.EqualTo(new Vector3F(7, -1, 0)).Using<Vector3F>(Near), "a node moved by hand stays where it was put");
            Assert.That(knee.Transform.GetLocalMatrixF().TranslationVector, Is.EqualTo(new Vector3F(3, -1, 0)).Using<Vector3F>(Near));
        });
    }

    // A skin may have as many joints as its file gives: a vertex bound to one far along a long skeleton goes where that
    // joint carries it.
    [Test]
    public void AJointFarAlongALongSkeleton_CarriesItsVertex()
    {
        Import(SkinnedScene(), out var leg, out var hip, out var knee);
        var skin = leg.GetComponent<Skin>();
        skin.Skeleton.Joints.InsertRange(1, Enumerable.Repeat(hip, 198));
        skin.Skeleton.InverseBindMatrices.InsertRange(1, Enumerable.Repeat(Matrix4x4F.Identity, 198));
        var mesh = leg.GetComponent<MeshData>().Mesh;
        mesh.SetJointIndices([Vector4F.Zero, new Vector4F(199, 0, 0, 0), Vector4F.Zero]);
        mesh.SetJointWeights([Vector4F.UnitX, Vector4F.UnitX, Vector4F.UnitX]);
        knee.Transform.Position += new Vector3(10, 0, 0);

        var posed = skin.PosePoints(mesh);

        Assert.That(posed[1], Is.EqualTo(new Vector3F(11, 0, 0)).Using<Vector3F>(Near));
    }

    // A joint index below zero, which a signed accessor can hold, is taken as the first joint, as the draw takes it:
    // the hip carries the point three along, where the knee would carry it thirteen and no joint would leave it.
    [Test]
    public void AJointIndexBelowZero_IsTheFirstJoint()
    {
        Import(SkinnedScene(), out var leg, out var hip, out var knee);
        var mesh = leg.GetComponent<MeshData>().Mesh;
        mesh.SetJointIndices([new Vector4F(-1, 0, 0, 0), new Vector4F(-1, 0, 0, 0), new Vector4F(-1, 0, 0, 0)]);
        mesh.SetJointWeights([Vector4F.UnitX, Vector4F.UnitX, Vector4F.UnitX]);
        hip.Transform.Position = new Vector3(3, 0, 0);
        knee.Transform.Position += new Vector3(10, 0, 0);

        var posed = leg.GetComponent<Skin>().PosePoints(mesh);

        Assert.That(posed[1], Is.EqualTo(new Vector3F(4, 0, 0)).Using<Vector3F>(Near));
    }

    // glTF forbids a weight below zero; one is taken as none, as the draw takes it, so the box round the pose holds.
    [Test]
    public void AWeightBelowZero_IsNone_AndThePoseStaysInsideItsBox()
    {
        Import(SkinnedScene(), out var leg, out var hip, out _);
        var mesh = leg.GetComponent<MeshData>().Mesh;
        mesh.SetJointWeights([new Vector4F(1.5f, -0.5f, 0, 0), new Vector4F(1.5f, -0.5f, 0, 0), new Vector4F(1.5f, -0.5f, 0, 0)]);
        hip.Transform.Position = new Vector3(4, 0, 0);
        var skin = leg.GetComponent<Skin>();

        var posed = skin.PosePoints(mesh);
        var box = skin.PosedBounds(mesh);

        Assert.Multiple(() =>
        {
            Assert.That(posed[1], Is.EqualTo(new Vector3F(5, 0, 0)).Using<Vector3F>(Near), "carried by the hip alone");
            Assert.That(posed, Has.All.Matches<Vector3F>(p => box.Contains(p) != ContainmentType.Disjoint));
        });
    }

    // The loose box an index keeps, from where the joints stand alone, holds every point of the pose in the world - with
    // the joints moved, turned and stretched unevenly, and a point bent by none.
    [Test]
    public void TheWorldBox_HoldsThePose_HoweverTheJointsStand()
    {
        Import(SkinnedScene(), out var leg, out var hip, out var knee);
        var mesh = leg.GetComponent<MeshData>().Mesh;
        mesh.SetJointWeights([Vector4F.UnitX, new Vector4F(0.3f, 0.7f, 0, 0), Vector4F.Zero]);
        hip.Transform.Position = new Vector3(2, -1, 5);
        hip.Transform.BaseScale = new Vector3F(1, 3, 0.5f);
        knee.Transform.Rotation = QuaternionF.RotationYawPitchRoll(0.7f, -0.4f, 1.1f);
        knee.Transform.Position += new Vector3(1, 2, 0);
        var skin = leg.GetComponent<Skin>();

        var world = leg.Transform.GetWorldMatrixF();
        var posed = skin.PosePoints(mesh).Select(p => Vector3F.TransformCoordinate(p, world)).ToList();
        var box = skin.WorldBounds(mesh, null);

        Assert.That(posed, Has.All.Matches<Vector3F>(p => box.Contains(p) != ContainmentType.Disjoint));
    }

    // A pose that holds is looked through with a tree fitted to it; when the joints move again, the tree follows.
    [Test]
    public void ARay_FindsTheSkinWhereItStands_AsItStandsStill_AndAsItMoves()
    {
        Import(SkinnedScene(), out var leg, out var hip, out _);
        var mesh = leg.GetComponent<MeshData>().Mesh;
        var skin = leg.GetComponent<Skin>();
        var down = new Ray(new Vector3F(0.25f, -0.25f, -5), Vector3F.UnitZ);
        var movedDown = new Ray(new Vector3F(4.25f, -0.25f, -5), Vector3F.UnitZ);

        var atRest = skin.Intersect(mesh, down, out _);
        hip.Transform.Position = new Vector3(4, 0, 0);
        var whereItWas = skin.Intersect(mesh, down, out _);
        var whereItWent = skin.Intersect(mesh, movedDown, out var distance);
        var again = skin.Intersect(mesh, movedDown, out _);

        Assert.Multiple(() =>
        {
            Assert.That(atRest, Is.True);
            Assert.That(whereItWas, Is.False);
            Assert.That(whereItWent, Is.True);
            Assert.That(distance, Is.EqualTo(5).Within(1e-4f));
            Assert.That(again, Is.True);
        });
    }

    [Test]
    public void ADuplicate_IsShownAndSwitchedAsTheOriginal()
    {
        var model = Import(SkinnedScene(), out var leg, out _, out _);
        leg.Visible = false;
        leg.IsEnabled = false;
        leg.IgnoreInCollisionDetection = true;

        var copiedLeg = model.Duplicate().Dependencies.Single(e => e.Name.StartsWith("Leg"));

        Assert.Multiple(() =>
        {
            Assert.That(copiedLeg.Visible, Is.False);
            Assert.That(copiedLeg.IsEnabled, Is.False);
            Assert.That(copiedLeg.IgnoreInCollisionDetection, Is.True);
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

    private static AppTime At(double seconds) => new() { TotalTime = TimeSpan.FromSeconds(seconds) };

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
