using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Searches of a simulation's bodies: ready conditions, Where, the answers and the actions on what is found.
/// </summary>
[TestFixture]
public class FindTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    private PhysicsSimulation _simulation;
    private Layer _debris;
    private BodyId _wall;
    private BodyId _light;
    private BodyId _heavy;
    private BodyId _piece;
    private BodyId _platform;

    // In no gravity: a static wall, a kinematic platform, two dynamic crates of the default layer - one heavy - and a
    // piece of debris, all along X.
    [SetUp]
    public void SetUp()
    {
        _simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        _debris = _simulation.Layers.Define("Debris");
        _wall = _simulation.NewBody().Static().WithShape(Crate).At(new WorldPosition(-6, 0, 0)).Add();
        _platform = _simulation.NewBody().Kinematic().WithShape(Crate).At(new WorldPosition(-3, 0, 0)).Add();
        _light = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 0, 0)).WithMass(10).Add();
        _heavy = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(3, 0, 0)).WithMass(500).Add();
        _piece = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(6, 0, 0)).WithMass(1).OnLayer(_debris)
            .NeverSleeping().Add();
        _simulation.Step();
    }

    [TearDown]
    public void TearDown() => _simulation.Dispose();

    [Test]
    public void ReadyConditions_KeepTheBodiesTheyTell()
    {
        var find = _simulation.Find();
        var all = Found(find);
        var dynamic = Found(find.Dynamic());
        var standing = Found(find.Static().Kinematic());
        var debris = Found(find.OnLayers(LayerMask.Of(_debris)));
        var butHeavy = Found(find.Dynamic().Except(_heavy));
        var heavier = Found(find.Heavier(than: 5));
        var inBox = Found(find.Within(Box(-4, 1)));
        var inSphere = Found(find.Within(new BoundingSphere<double>(new Vector3<double>(3, 0, 0), 3.5)));
        var awake = Found(find.Awake().Dynamic());
        var anyAsleep = find.Asleep().Dynamic().Any();

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EquivalentTo(new[] { _wall, _platform, _light, _heavy, _piece }));
            Assert.That(dynamic, Is.EquivalentTo(new[] { _light, _heavy, _piece }));
            Assert.That(standing, Is.EquivalentTo(new[] { _wall, _platform }));
            Assert.That(debris, Is.EquivalentTo(new[] { _piece }));
            Assert.That(butHeavy, Is.EquivalentTo(new[] { _light, _piece }));
            Assert.That(heavier, Is.EquivalentTo(new[] { _light, _heavy }));
            Assert.That(inBox, Is.EquivalentTo(new[] { _platform, _light }));
            Assert.That(inSphere, Is.EquivalentTo(new[] { _light, _heavy, _piece }));
            Assert.That(awake, Is.EquivalentTo(new[] { _light, _heavy, _piece }));
            Assert.That(anyAsleep, Is.False);
        });
    }

    [Test]
    public void Where_KeepsWhatAFilterAStaticLambdaOrTheQuerySyntaxKeeps()
    {
        var byStruct = Found(_simulation.Find().Where(new HeavierThan(5)));
        var byLambda = Found(_simulation.Find().Where(5f, static (body, mass) => body.Mass > mass));
        var bySyntax = new List<BodyId>();
        foreach (var body in from body in _simulation.Find().Dynamic() where body.Layer.Index == 0 select body)
            bySyntax.Add(body.Id);

        Assert.Multiple(() =>
        {
            Assert.That(byStruct, Is.EquivalentTo(new[] { _light, _heavy }));
            Assert.That(byLambda, Is.EquivalentTo(new[] { _light, _heavy }));
            Assert.That(bySyntax, Is.EquivalentTo(new[] { _light, _heavy }));
        });
    }

    [Test]
    public void NearestTo_AnswersNearestFirst_KeepingTheNearestThatFit()
    {
        Span<BodyId> all = stackalloc BodyId[8];
        Span<BodyId> two = stackalloc BodyId[2];
        var near = new WorldPosition(4, 0, 0);

        var count = _simulation.Find().NearestTo(near).Into(all);
        var allNearest = all[..count].ToArray();
        var twoCount = _simulation.Find().NearestTo(near).Into(two);
        var twoNearest = two[..twoCount].ToArray();

        var nearestDynamic = _simulation.Find().Dynamic().NearestTo(new WorldPosition(-10, 0, 0)).First();
        var none = _simulation.Find().Heavier(than: 1000).First();
        var dynamicCount = _simulation.Find().Dynamic().Count();

        Assert.Multiple(() =>
        {
            Assert.That(allNearest, Is.EqualTo(new[] { _heavy, _piece, _light, _platform, _wall }));
            Assert.That(twoNearest, Is.EqualTo(new[] { _heavy, _piece }));
            Assert.That(nearestDynamic, Is.EqualTo(_light));
            Assert.That(none, Is.EqualTo(BodyId.None));
            Assert.That(dynamicCount, Is.EqualTo(3));
        });
    }

    // Each action reaches every body found that can take it: an impulse only the dynamic, a velocity all that move.
    [Test]
    public void ActionsOnFoundBodies_ReachEachThatCanTakeThem()
    {
        _simulation.Find()
            .ApplyImpulse(new Vector3F(0, 10, 0))
            .SetLayer(_debris);
        _simulation.Find().Kinematic().SetLinearVelocity(new Vector3F(1, 0, 0));
        _simulation.Step();
        var onDebris = _simulation.Find().OnLayers(LayerMask.Of(_debris)).Count();

        Assert.Multiple(() =>
        {
            Assert.That(_simulation.LinearVelocityOf(_light).Y, Is.EqualTo(1).Within(1e-4f));
            Assert.That(_simulation.LinearVelocityOf(_heavy).Y, Is.EqualTo(0.02f).Within(1e-4f));
            Assert.That(_simulation.LinearVelocityOf(_platform), Is.EqualTo(new Vector3F(1, 0, 0)));
            Assert.That(_simulation.PoseOf(_wall).Position, Is.EqualTo(new WorldPosition(-6, 0, 0)), "struck by nothing");
            Assert.That(onDebris, Is.EqualTo(5));
        });
    }

    // A search only reading answers as any, a stretch kept, and refuses every action.
    [Test]
    public void ASearchOnlyReading_AnswersButRefusesActions()
    {
        var reading = _simulation.Find().Dynamic().AsReadOnly();
        var count = reading.Count();
        var nearest = _simulation.Find().Dynamic().AsReadOnly().NearestTo(new WorldPosition(-10, 0, 0)).First();
        var firstTwo = _simulation.Find().Dynamic().Take(2).AsReadOnly().Count();

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(nearest, Is.EqualTo(_light));
            Assert.That(firstTwo, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().AsReadOnly().WakeUp());
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().AsReadOnly().ApplyImpulse(Vector3F.UnitY));
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().AsReadOnly().SetLinearVelocity(Vector3F.UnitY));
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().AsReadOnly().SetAngularVelocity(Vector3F.UnitY));
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().AsReadOnly().SetLayer(_debris));
            Assert.Throws<InvalidOperationException>(() =>
                _simulation.Find().AsReadOnly().SetMaterial(PhysicsMaterial.Default));
            Assert.Throws<InvalidOperationException>(() =>
                _simulation.Find().AsReadOnly().Where(new HeavierThan(5)).RemoveBodies());
        });
        Assert.That(_simulation.Find().Count(), Is.EqualTo(5), "nothing taken out");
    }

    [Test]
    public void FoundBodies_AreTakenOutTogether_AndAWalkTakingThemOutMissesNone()
    {
        _simulation.Find().Dynamic().Heavier(than: 5).RemoveBodies();
        var taken = new List<BodyId>();
        foreach (var body in _simulation.Find())
        {
            taken.Add(body.Id);
            _simulation.RemoveBody(body.Id);
        }

        _simulation.Step();
        var anyLeft = _simulation.Find().Any();

        Assert.Multiple(() =>
        {
            Assert.That(_simulation.Contains(_light) || _simulation.Contains(_heavy), Is.False);
            Assert.That(taken, Is.EquivalentTo(new[] { _wall, _platform, _piece }));
            Assert.That(anyLeft, Is.False);
        });
    }

    [Test]
    public void ConditionsToldAgain_Narrow()
    {
        var heavier = Found(_simulation.Find().Heavier(than: 100).Heavier(than: 5));
        var onBoth = Found(_simulation.Find().OnLayers(LayerMask.Of(_debris)).OnLayers(LayerMask.All));

        Assert.Multiple(() =>
        {
            Assert.That(heavier, Is.EquivalentTo(new[] { _heavy }));
            Assert.That(onBoth, Is.EquivalentTo(new[] { _piece }));
            Assert.Throws<ArgumentOutOfRangeException>(() => _simulation.Find().Heavier(float.NaN).Count());
        });
    }

    // A crate asked to be static before the blast's step takes no impulse, and the blast goes on to the rest.
    [Test]
    public void ARadialImpulse_PassesByABodyAskedToStopBeingDynamic()
    {
        _simulation.SetKind(_light, BodyKind.Static);
        _simulation.ApplyRadialImpulse(new WorldPosition(1, 0, 0), 5, 100);
        _simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(_simulation.KindOf(_light), Is.EqualTo(BodyKind.Static));
            Assert.That(_simulation.LinearVelocityOf(_heavy).X, Is.GreaterThan(0), "struck all the same");
        });
    }

    // Walked from the last added, the walk takes out the first added on its way: the last takes its place, walked
    // already, and is not walked again.
    [Test]
    public void AWalkTakingOutAnotherBody_WalksNoneTwice()
    {
        var walked = new List<BodyId>();
        foreach (var body in _simulation.Find())
        {
            walked.Add(body.Id);
            if (body.Id == _piece)
                _simulation.RemoveBody(_wall);
        }

        Assert.That(walked, Is.EquivalentTo(new[] { _piece, _heavy, _light, _platform }));
    }

    // A search sees what the last step read: a body added is found once a step takes it, one taken out is not.
    [Test]
    public void ASearch_SeesTheBodiesTheLastStepRead()
    {
        var added = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 5, 0)).Add();
        var beforeTheStep = Found(_simulation.Find()).Contains(added);
        _simulation.RemoveBody(_light);
        var afterRemoval = Found(_simulation.Find()).Contains(_light);
        _simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(beforeTheStep, Is.False);
            Assert.That(afterRemoval, Is.False);
            Assert.That(Found(_simulation.Find()).Contains(added), Is.True);
        });
    }

    // Chains, searches and their actions, once the simulation's own lists have grown to them, take nothing.
    [Test]
    public void ChainsAndSearches_AllocateNothing()
    {
        void Frame()
        {
            Span<BodyId> nearest = stackalloc BodyId[4];
            _simulation.Body(_light).SetLinearVelocity(new Vector3F(0.1f, 0, 0)).ApplyImpulse(new Vector3F(0, 0.1f, 0));
            _simulation.Find().Dynamic().Where(new HeavierThan(5)).Within(Box(-10, 10)).WakeUp();
            _simulation.Find().Where(1f, static (body, mass) => body.Mass > mass).NearestTo(WorldPosition.Zero).Into(nearest);
            _simulation.Query().OnLayers(LayerMask.Of(_debris)).RayCast(new WorldPosition(6, 5, 0), -Vector3F.UnitY, 10,
                out _);
            _simulation.World().ApplyRadialImpulse(WorldPosition.Zero, 4, 0.5f).Step();
        }

        // Long enough for the bodies pushed apart to have met all the core's lists will hold: a step grows one once,
        // some fifty frames in.
        for (var frame = 0; frame < 400; frame++)
            Frame();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 200; frame++)
            Frame();

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // A body put elsewhere is found in its new area once that step is read, not in its old one; one moving too.
    [Test]
    public void AnAreasSearch_FindsABodyWhereTheLastStepLeftIt()
    {
        _simulation.SetPose(_light, new Pose(new WorldPosition(0, 20, 0)));
        var beforeTheStep = Found(_simulation.Find().Within(Box(-1, 1))).Contains(_light);
        _simulation.Step();
        var oldArea = Found(_simulation.Find().Within(Box(-1, 1))).Contains(_light);
        var newArea = Found(_simulation.Find().Within(new BoundingSphere<double>(new Vector3<double>(0, 20, 0), 1)))
            .Contains(_light);
        _simulation.SetLinearVelocity(_heavy, new Vector3F(0, 0, 60));
        for (var step = 0; step < _simulation.Settings.StepRate; step++)
            _simulation.Step();

        var movedOn = Found(_simulation.Find().Within(new BoundingSphere<double>(new Vector3<double>(3, 0, 60), 2)))
            .Contains(_heavy);

        Assert.Multiple(() =>
        {
            Assert.That(beforeTheStep, Is.True, "where the last step left it");
            Assert.That(oldArea, Is.False);
            Assert.That(newArea, Is.True);
            Assert.That(movedOn, Is.True);
        });
    }

    [Test]
    public void AnAreaThatIsNotFinite_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _simulation.Find().Within(
                new BoundingBox<double>(new Vector3<double>(double.NaN), new Vector3<double>(1))).Count());
            Assert.Throws<ArgumentOutOfRangeException>(() => _simulation.Find().Within(
                new BoundingSphere<double>(Vector3<double>.Zero, -1)).Count());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _simulation.Find().NearestTo(new WorldPosition(double.PositiveInfinity, 0, 0)).Count());
        });
    }

    private static List<BodyId> Found<TFilter>(BodyFinder<TFilter> find)
        where TFilter : IBodyFilter
    {
        var found = new List<BodyId>();
        foreach (var body in find)
            found.Add(body.Id);

        return found;
    }

    private static BoundingBox<double> Box(double fromX, double toX) =>
        new(new Vector3<double>(fromX, -1, -1), new Vector3<double>(toX, 1, 1));

    private readonly struct HeavierThan(float mass) : IBodyFilter
    {
        public bool Matches(in BodyView body) => body.Mass > mass;
    }
}
