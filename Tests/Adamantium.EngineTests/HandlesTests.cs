using System;
using System.Linq;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Tools;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class HandlesTests
{
    private static readonly Vector3<double> At = new(0, 0, 10);

    [Test]
    public void Move_AlongAnAxis_FollowsThePointer()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new MoveHandles();

        Drag(scene, handles, target, "RightAxis", At + new Vector3<double>(0.5, 0, 0), At + new Vector3<double>(2.5, 0, 0));

        AssertNear(target.Transform.Position, At + new Vector3<double>(2, 0, 0));
    }

    [Test]
    public void Move_AcrossAPlane_FollowsThePointer()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new MoveHandles();

        Drag(scene, handles, target, "RightUpManipulator", At + new Vector3<double>(0.5, 0.5, 0), At + new Vector3<double>(1.5, -1, 0));

        AssertNear(target.Transform.Position, At + new Vector3<double>(1, -1.5, 0));
    }

    [Test]
    public void Move_ArmAtTheEye_AndSquaresEdgeOn_AreHidden()
    {
        var scene = new ToolScene();
        var handles = new MoveHandles();

        handles.Place(ToolScene.Target(At), scene.Camera);

        Assert.That(IsShown(handles, "ForwardAxis", scene), Is.False);
        Assert.That(IsShown(handles, "ForwardAxisManipulator", scene), Is.False);
        Assert.That(IsShown(handles, "RightForwardManipulator", scene), Is.False);
        Assert.That(IsShown(handles, "UpForwardManipulator", scene), Is.False);
        Assert.That(IsShown(handles, "RightAxis", scene), Is.True);
        Assert.That(IsShown(handles, "UpAxisManipulator", scene), Is.True);
        Assert.That(IsShown(handles, "RightUpManipulator", scene), Is.True);
    }

    [Test]
    public void Move_Squares_SitBetweenTheirArrows_FromEverySide()
    {
        var scene = new ToolScene();
        var at = new Vector3<double>(4, 4, 10);
        var handles = new MoveHandles();

        handles.Place(ToolScene.Target(at), scene.Camera);

        Assert.That(SideOf(CenterOf(handles, "RightUpManipulator", scene) - at), Is.EqualTo(new[] { 1, 1, 0 }));
        Assert.That(SideOf(CenterOf(handles, "RightForwardManipulator", scene) - at), Is.EqualTo(new[] { 1, 0, 1 }));
        Assert.That(SideOf(CenterOf(handles, "UpForwardManipulator", scene) - at), Is.EqualTo(new[] { 0, 1, 1 }));
    }

    [Test]
    public void Move_ASquareAtAGrazingAngle_IsHidden_AsAnArmNearTheEyeIs()
    {
        var scene = new ToolScene();
        var handles = new MoveHandles();

        handles.Place(ToolScene.Target(new Vector3<double>(0, 1.5, 10)), scene.Camera);
        Assert.That(IsShown(handles, "RightForwardManipulator", scene), Is.False, "8.5 degrees off edge-on");

        handles.Place(ToolScene.Target(new Vector3<double>(0, 3.64, 10)), scene.Camera);
        Assert.That(IsShown(handles, "RightForwardManipulator", scene), Is.True, "20 degrees off edge-on");
    }

    [Test]
    public void Move_AcrossAPlane_ByItsOutline_StaysInThatPlane()
    {
        var scene = new ToolScene();
        scene.LookFrom(At + new Vector3<double>(-6, -5, -8), At);
        var target = ToolScene.Target(At);
        var handles = new MoveHandles();

        Drag(scene, handles, target, "RightForwardOutline", At + new Vector3<double>(0.3, 0, 0.3), At + new Vector3<double>(1.3, 0, 0.8));

        AssertNear(target.Transform.Position, At + new Vector3<double>(1, 0, 0.5));
    }

    [Test]
    public void Move_PointingAtASquaresOutline_LightsUpTheWholeSquare()
    {
        var handles = new MoveHandles();

        handles.Highlight(handles.Shape.Get("RightForwardOutline"));

        Assert.That(handles.Shape.Get("RightForwardManipulator").IsSelected, Is.True);
        Assert.That(handles.Shape.Get("RightForwardOutline").IsSelected, Is.True);
    }

    [Test]
    public void Move_Squares_StartWhereTheArrowsStart()
    {
        var handles = new MoveHandles();
        var fill = handles.Shape.Get("RightUpManipulator").GetComponent<MeshData>().Mesh.Points;
        var arrowStart = handles.Shape.Get("RightAxis").GetComponent<MeshData>().Mesh.Points.Min(p => p.X);

        Assert.That(fill.Min(p => p.X), Is.EqualTo(arrowStart).Within(1e-9));
        Assert.That(fill.Min(p => p.Y), Is.EqualTo(arrowStart).Within(1e-9));
    }

    [Test]
    public void Move_SquareOutline_ClosesRoundItsFill()
    {
        var handles = new MoveHandles();
        var fill = handles.Shape.Get("RightUpManipulator").GetComponent<MeshData>().Mesh;
        var outline = handles.Shape.Get("RightUpOutline").GetComponent<MeshData>().Mesh;

        Assert.That(outline.Indices, Is.EqualTo(new[] { 0, 1, 2, 3, 0 }));
        Assert.That(outline.Points, Is.EquivalentTo(fill.Points));
    }

    [Test]
    public void Rotate_Rings_AreLinesInTheirOwnPlanes()
    {
        var handles = new RotationHandles();

        AssertRing(handles, "RightAxisOrbit", p => p.X);
        AssertRing(handles, "UpAxisOrbit", p => p.Y);
        AssertRing(handles, "ForwardAxisOrbit", p => p.Z);
    }

    [Test]
    public void Rotate_AlongARing_TurnsAsFarAsThePointerGoesRoundIt()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        var along = Vector2F.Normalize(scene.PixelOf(At + new Vector3<double>(radius, radius * 0.01, 0)) - grab);

        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(grab + along * 20));

        var turned = Vector3F.TransformNormal(Vector3F.UnitX, target.Transform.GetLocalMatrixF());
        var center = scene.PixelOf(At);
        Assert.That(Math.Atan2(turned.Y, turned.X), Is.EqualTo(Math.Atan2(20, (grab - center).Length())).Within(1e-3));
        Assert.That(turned.Z, Is.EqualTo(0).Within(1e-4));
        AssertNear(target.Transform.Position, At);
    }

    // Following the ring round keeps turning: by the tangent where it was grabbed, a pointer past a quarter turn went
    // back and turned the target back with it.
    [Test]
    public void Rotate_GoingRoundARing_KeepsTurning_PastHalfATurn()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));

        for (var degrees = 30; degrees <= 240; degrees += 30)
        {
            var angle = float.DegreesToRadians((float)degrees);
            handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(radius * Math.Cos(angle), radius * Math.Sin(angle), 0))));
        }

        var turned = Vector3F.TransformNormal(Vector3F.UnitX, target.Transform.GetLocalMatrixF());
        var expected = float.DegreesToRadians(240f);
        Assert.That(Math.Abs(Math.Atan2(turned.Y, turned.X)), Is.EqualTo(Math.PI * 2 - expected).Within(1e-2));
        Assert.That(Math.Abs(QuaternionF.Dot(target.Transform.Rotation, Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, expected))),
            Is.EqualTo(1).Within(1e-3));
    }

    // Pulled far out along the ring, the pointer keeps turning as fast: by the angle round the center, a pointer
    // far away turned the target slower and slower, and then not at all.
    [Test]
    public void Rotate_PulledFarAlongARing_KeepsTurningAsFast()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        var along = Vector2F.Normalize(scene.PixelOf(At + new Vector3<double>(radius, radius * 0.01, 0)) - grab);
        var radiusInPixels = (grab - scene.PixelOf(At)).Length();
        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));

        var turns = new double[3];
        for (var step = 1; step <= 60; step++)
        {
            handles.Drag(target, scene.RayAt(grab + along * (radiusInPixels * step / 10)));
            if (step is 40 or 50 or 60)
            {
                var turned = Vector3F.TransformNormal(Vector3F.UnitX, target.Transform.GetLocalMatrixF());
                turns[step / 10 - 4] = Math.Atan2(turned.Y, turned.X);
            }
        }

        var fifth = Math.IEEERemainder(turns[1] - turns[0], 2 * Math.PI);
        var sixth = Math.IEEERemainder(turns[2] - turns[1], 2 * Math.PI);
        Assert.Multiple(() =>
        {
            Assert.That(sixth, Is.GreaterThan(0.5), "the sixth radius pulled still turns");
            Assert.That(sixth, Is.EqualTo(fifth).Within(1e-3), "as fast as the fifth");
        });
    }

    // A ring seen at a slant is an ellipse on screen: following it round still turns as far as the pointer went round.
    [Test]
    public void Rotate_GoingRoundATiltedRing_TurnsAsFarAsItWent()
    {
        var scene = new ToolScene();
        scene.LookFrom(At + new Vector3<double>(10 * 0.6, 0, -10 * 0.8), At);
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "RightAxisOrbit", scene);
        var start = float.DegreesToRadians(-60f);
        var grab = scene.PixelOf(At + new Vector3<double>(0, radius * Math.Cos(start), radius * Math.Sin(start)));
        handles.BeginDrag(target, handles.Shape.Get("RightAxisOrbit"), scene.RayAt(grab));

        for (var degrees = -30; degrees <= 210; degrees += 30)
        {
            var angle = float.DegreesToRadians((float)degrees);
            handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(0, radius * Math.Cos(angle), radius * Math.Sin(angle)))));
        }

        var turn = float.DegreesToRadians(270f);
        Assert.That(Math.Abs(QuaternionF.Dot(target.Transform.Rotation, Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, turn))),
            Is.EqualTo(1).Within(1e-3));
    }

    // Out past two radii and round, then back near the ring: no lurch on the way back in.
    [Test]
    public void Rotate_BackNearTheRingFromFarOut_TurnsOnWithoutALurch()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(radius * 3, 0, 0))));
        for (var degrees = 10; degrees <= 150; degrees += 10)
        {
            var angle = float.DegreesToRadians((float)degrees);
            handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(radius * 3 * Math.Cos(angle), radius * 3 * Math.Sin(angle), 0))));
        }

        var before = target.Transform.Rotation;
        var back = float.DegreesToRadians(150f);
        handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(radius * Math.Cos(back), radius * Math.Sin(back), 0))));

        var lurch = 2 * Math.Acos(Math.Clamp(Math.Abs(QuaternionF.Dot(before, target.Transform.Rotation)), 0, 1));
        Assert.That(lurch, Is.LessThan(0.05));
    }

    [Test]
    public void Rotate_ThroughTheRingsCenter_DoesNotSpin()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        var center = scene.PixelOf(At);
        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(center + new Vector2F(1, 0.3f)));
        var before = target.Transform.Rotation;

        handles.Drag(target, scene.RayAt(center + new Vector2F(0, -0.3f)));

        var spin = 2 * Math.Acos(Math.Clamp(Math.Abs(QuaternionF.Dot(before, target.Transform.Rotation)), 0, 1));
        Assert.That(spin, Is.LessThan(0.01));
    }

    [Test]
    public void Rotate_GoingRoundARing_TheOtherWay_TurnsTheOtherWay()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "ForwardAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(radius, 0, 0));
        handles.BeginDrag(target, handles.Shape.Get("ForwardAxisOrbit"), scene.RayAt(grab));

        for (var degrees = -30; degrees >= -240; degrees -= 30)
        {
            var angle = float.DegreesToRadians((float)degrees);
            handles.Drag(target, scene.RayAt(scene.PixelOf(At + new Vector3<double>(radius * Math.Cos(angle), radius * Math.Sin(angle), 0))));
        }

        var expected = float.DegreesToRadians(-240f);
        Assert.That(Math.Abs(QuaternionF.Dot(target.Transform.Rotation, Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, expected))),
            Is.EqualTo(1).Within(1e-3));
    }

    // A flat view sees every ring along its own forward: a ring off the middle of the view, edge-on there, is a line to
    // drag along, not a circle to go round.
    [Test]
    public void Rotate_ARingEdgeOnInAFlatView_OffTheMiddle_StillTurns()
    {
        var scene = new ToolScene();
        scene.Camera.ProjectionTransitionTime = 0;
        scene.Camera.ProjectionType = Adamantium.ECS.Components.CameraProjectionType.Ortho;
        scene.Camera.Update(new AppTime());
        var at = new Vector3<double>(6, 0, 10);
        var target = ToolScene.Target(at);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "RightAxisOrbit", scene);
        var grab = scene.PixelOf(at + new Vector3<double>(0, radius * 0.5, -radius * Math.Sqrt(0.75)));
        var along = Vector2F.Normalize(scene.PixelOf(at) - grab);

        handles.BeginDrag(target, handles.Shape.Get("RightAxisOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(grab + along * 20));

        var turned = Vector3F.TransformNormal(Vector3F.UnitY, target.Transform.GetLocalMatrixF());
        Assert.That(Math.Abs(Math.Atan2(turned.Z, turned.Y)), Is.EqualTo(20.0 / handles.Pixels).Within(2e-3));
    }

    [Test]
    public void Rotate_ARingSeenEdgeOn_StillTurns()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "RightAxisOrbit", scene);
        var grab = scene.PixelOf(At + new Vector3<double>(0, radius * 0.5, -radius * Math.Sqrt(0.75)));
        var along = Vector2F.Normalize(scene.PixelOf(At) - grab);

        handles.BeginDrag(target, handles.Shape.Get("RightAxisOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(grab + along * 20));

        var turned = Vector3F.TransformNormal(Vector3F.UnitY, target.Transform.GetLocalMatrixF());
        Assert.That(Math.Abs(Math.Atan2(turned.Z, turned.Y)), Is.EqualTo(20.0 / handles.Pixels).Within(1e-3));
        Assert.That(turned.X, Is.EqualTo(0).Within(1e-4));
    }

    [Test]
    public void Rotate_TheBall_TurnsEvenlyFromTheBlackCircleInward()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new RotationHandles();
        handles.Place(target, scene.Camera);
        var direction = Vector2F.Normalize(new Vector2F(1, 1));
        var start = scene.PixelOf(At) + direction * handles.Pixels * 1.05f;
        handles.BeginDrag(target, handles.Shape.Get("CentralManipulator"), scene.RayAt(start));

        var previous = 0.0;
        var step = 0.0;
        for (int i = 1; i <= 12; i++)
        {
            handles.Drag(target, scene.RayAt(start - direction * (2 * i)));
            var angle = 2 * Math.Acos(Math.Clamp(Math.Abs(target.Transform.Rotation.W), 0, 1));
            var now = angle - previous;
            Assert.That(now, Is.GreaterThan(0), $"step {i}");
            if (i > 1)
            {
                Assert.That(now, Is.LessThan(step * 1.5), $"step {i}: {now} after {step}");
            }

            step = now;
            previous = angle;
        }
    }

    [Test]
    public void Rotate_Pick_TakesWhatIsDrawnNearestThePointer()
    {
        var scene = new ToolScene();
        var handles = new RotationHandles();
        handles.Place(ToolScene.Target(At), scene.Camera);
        var center = scene.PixelOf(At);
        var diagonal = Vector2F.Normalize(new Vector2F(1, 1)) * handles.Pixels;

        Assert.That(PickedAt(handles, scene, center + diagonal * 1.125f), Is.EqualTo("CurrentViewManipulator"));
        Assert.That(PickedAt(handles, scene, center + diagonal * 1.05f), Is.EqualTo("CentralManipulator"));
        Assert.That(PickedAt(handles, scene, center + diagonal), Is.EqualTo("ForwardAxisOrbit"));
        Assert.That(PickedAt(handles, scene, center + diagonal * 0.5f), Is.EqualTo("CentralManipulator"));
        Assert.That(PickedAt(handles, scene, center + diagonal * 1.3f), Is.Null);
    }

    [Test]
    public void Rotate_Pick_NeverTakesTheHalfOfARingThatIsNotDrawn()
    {
        AssertPicksOnlyDrawnHalves(new RotationHandles(), "AxisOrbit", 120);
    }

    [Test]
    public void Pivot_Pick_NeverTakesTheHalfOfARingThatIsNotDrawn()
    {
        AssertPicksOnlyDrawnHalves(new PivotHandles(), "Orbit", 70);
    }

    [Test]
    public void Pivot_ARingSeenEdgeOn_StillTurnsThePivot()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new PivotHandles();
        handles.Place(target, scene.Camera);
        var radius = RingRadius(handles, "RightOrbit", scene);
        var radiusPixels = (scene.PixelOf(At + new Vector3<double>(0, radius, 0)) - scene.PixelOf(At)).Length();
        var grab = scene.PixelOf(At + new Vector3<double>(0, radius * 0.5, -radius * Math.Sqrt(0.75)));
        var along = Vector2F.Normalize(scene.PixelOf(At) - grab);

        handles.BeginDrag(target, handles.Shape.Get("RightOrbit"), scene.RayAt(grab));
        handles.Drag(target, scene.RayAt(grab + along * 20));

        var turned = Vector3F.Transform(Vector3F.UnitY, target.Transform.PivotRotation);
        Assert.That(Math.Abs(Math.Atan2(turned.Z, turned.Y)), Is.EqualTo(20.0 / radiusPixels).Within(1).Percent);
        Assert.That(turned.X, Is.EqualTo(0).Within(1e-4));
        AssertNear(target.Transform.Pivot, At);
    }

    [Test]
    public void Scale_AlongAnAxis_StretchesOnlyThatAxis()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        var handles = new ScaleHandles();

        Drag(scene, handles, target, "RightAxis", At + new Vector3<double>(1, 0, 0), At + new Vector3<double>(2, 0, 0));

        AssertNear(target.Transform.ScaleFactor.As<double>(), new Vector3<double>(2, 1, 1));
    }

    [Test]
    public void Pivot_MovesThePivot_AndLeavesTheEntityInPlace()
    {
        var scene = new ToolScene();
        var target = ToolScene.Target(At);
        target.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(30));
        var before = Vector3F.TransformCoordinate(new Vector3F(1, 2, 3), target.Transform.GetWorldMatrixF());
        var handles = new PivotHandles();

        Drag(scene, handles, target, "MoveUp", At + new Vector3<double>(0, 0.6, 0), At + new Vector3<double>(0, 2.6, 0));

        AssertNear(target.Transform.Pivot, At + new Vector3<double>(0, 2, 0));
        var after = Vector3F.TransformCoordinate(new Vector3F(1, 2, 3), target.Transform.GetWorldMatrixF());
        AssertNear(after.As<double>(), before.As<double>());
    }

    [Test]
    public void PointLight_Anchor_DragsTheRange()
    {
        var scene = new ToolScene();
        var light = ToolScene.Target(At);
        light.AddComponent(new Light(LightType.Point));
        var handles = new PointLightHandles();

        Drag(scene, handles, light, "AnchorPointRight", At + new Vector3<double>(1, 0, 0), At + new Vector3<double>(3, 0, 0));

        Assert.That(light.GetComponent<Light>().Range, Is.EqualTo(3f).Within(1e-2f));
    }

    [Test]
    public void SpotLight_End_DragsTheRange_AndTheRimDragsTheAngle()
    {
        var scene = new ToolScene();
        var entity = ToolScene.Target(At);
        entity.AddComponent(new Light(LightType.Spot));
        var light = entity.GetComponent<Light>();
        var axis = Vector3F.Normalize(light.Direction).As<double>();
        var handles = new SpotLightHandles();

        Drag(scene, handles, entity, "AnchorPointCenter", At + axis, At + axis * 4);
        Assert.That(light.Range, Is.EqualTo(4f).Within(1e-2f));

        var baseCenter = At + axis * 4;
        Drag(scene, handles, entity, "AnchorPointRight", baseCenter + new Vector3<double>(light.SpotRadius, 0, 0), baseCenter + new Vector3<double>(4, 0, 0));
        Assert.That(light.OuterSpotAngle, Is.EqualTo(float.DegreesToRadians(45)).Within(1e-2f));
    }

    [Test]
    public void SpotLight_TurnedEntity_DragsAlongWhereItShines()
    {
        var scene = new ToolScene();
        var entity = ToolScene.Target(At);
        entity.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(180));
        entity.AddComponent(new Light(LightType.Spot));
        var light = entity.GetComponent<Light>();
        var axis = light.Direction.As<double>();
        var handles = new SpotLightHandles();

        Drag(scene, handles, entity, "AnchorPointCenter", At + axis, At + axis * 5);

        Assert.That(Vector3F.Dot(light.Direction, Vector3F.Up), Is.GreaterThan(0.999f));
        Assert.That(light.Range, Is.EqualTo(5f).Within(1e-2f));
    }

    [Test]
    public void LightHandles_ApplyOnlyToTheirKindOfLight()
    {
        var point = ToolScene.Target(At);
        point.AddComponent(new Light(LightType.Point));
        var spot = ToolScene.Target(At);
        spot.AddComponent(new Light(LightType.Spot));

        Assert.That(new PointLightHandles().AppliesTo(point), Is.True);
        Assert.That(new PointLightHandles().AppliesTo(spot), Is.False);
        Assert.That(new SpotLightHandles().AppliesTo(spot), Is.True);
        Assert.That(new SpotLightHandles().AppliesTo(ToolScene.Target(At)), Is.False);
    }

    private static void Drag(ToolScene scene, Handles handles, Entity target, string part, Vector3<double> from, Vector3<double> to)
    {
        handles.Place(target, scene.Camera);
        handles.BeginDrag(target, handles.Shape.Get(part), scene.RayAt(from));
        handles.Drag(target, scene.RayAt(to));
    }

    // Over a grid of pixels round the handles, seen at a slant: every ring the pick takes is taken on its drawn half.
    private static void AssertPicksOnlyDrawnHalves(Handles handles, string ringSuffix, int reach)
    {
        var scene = new ToolScene();
        scene.LookFrom(At + new Vector3<double>(-6, -5, -8), At);
        handles.Place(ToolScene.Target(At), scene.Camera);
        var center = scene.PixelOf(At);
        var rings = 0;

        for (int y = -reach; y <= reach; y += 3)
        {
            for (int x = -reach; x <= reach; x += 3)
            {
                var hit = handles.Pick(scene.RayAt(center + new Vector2F(x, y)), 6);
                if (!hit.IsHit || !hit.Entity.Name.EndsWith(ringSuffix))
                {
                    continue;
                }

                rings++;
                var ringCenter = hit.Entity.Transform.GetMetadata(scene.Camera).WorldMatrixF.TranslationVector;
                Assert.That(Vector3F.Dot(hit.Point - ringCenter, ringCenter), Is.LessThanOrEqualTo(1e-3f), $"{hit.Entity.Name} at {x},{y}");
            }
        }

        Assert.That(rings, Is.GreaterThan(0));
    }

    private static string PickedAt(Handles handles, ToolScene scene, Vector2F pixel)
    {
        var hit = handles.Pick(scene.RayAt(pixel), 6);
        return hit.IsHit ? hit.Entity.Name : null;
    }

    private static double RingRadius(Handles handles, string part, ToolScene scene)
    {
        var world = handles.Shape.Get(part).Transform.GetMetadata(scene.Camera).WorldMatrixF;
        var rim = Vector3F.TransformCoordinate(handles.Shape.Get(part).GetComponent<MeshData>().Mesh.Points[0].As<float>(), world);
        return (rim - world.TranslationVector).Length();
    }

    private static bool IsShown(Handles handles, string part, ToolScene scene)
    {
        return handles.Shape.Get(part).Transform.GetMetadata(scene.Camera).Enabled;
    }

    private static Vector3<double> CenterOf(Handles handles, string part, ToolScene scene)
    {
        var entity = handles.Shape.Get(part);
        var world = entity.Transform.GetMetadata(scene.Camera).WorldMatrixF;
        var points = entity.GetComponent<MeshData>().Mesh.Points;
        var sum = Vector3<double>.Zero;
        foreach (var point in points)
        {
            sum += Vector3F.TransformCoordinate(point.As<float>(), world).As<double>();
        }

        return sum / points.Length + scene.Camera.WorldPosition;
    }

    private static int[] SideOf(Vector3<double> offset)
    {
        return [Side(offset.X), Side(offset.Y), Side(offset.Z)];
    }

    private static int Side(double value)
    {
        return Math.Abs(value) < 1e-3 ? 0 : Math.Sign(value);
    }

    private static void AssertRing(Handles handles, string part, Func<Vector3<double>, double> across)
    {
        var mesh = handles.Shape.Get(part).GetComponent<MeshData>().Mesh;
        Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.LineList), part);
        Assert.That(mesh.Points.Length, Is.GreaterThan(8), part);
        foreach (var point in mesh.Points)
        {
            Assert.That(Math.Abs(across(point)), Is.LessThan(1e-4), $"{part}: {point}");
            Assert.That(point.Length(), Is.EqualTo(mesh.Points[0].Length()).Within(1e-3), $"{part}: {point}");
        }
    }

    private static void AssertNear(Vector3<double> actual, Vector3<double> expected)
    {
        Assert.That((actual - expected).Length(), Is.LessThan(1e-2), $"{actual} instead of {expected}");
    }
}
