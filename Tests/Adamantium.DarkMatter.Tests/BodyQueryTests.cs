using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The search's own LINQ operators and the query syntax over them: orders, groups, projections, joins, Skip and
/// Take, and aggregates - on the caller's buffers, nothing allocated.</summary>
[TestFixture]
public class BodyQueryTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    private PhysicsSimulation _simulation;
    private Layer _debris;
    private BodyId _wall;
    private BodyId _light;
    private BodyId _heavy;
    private BodyId _piece;
    private BodyId _shard;

    // In no gravity, along X: a static wall; on the default layer a 10 kg and a 500 kg crate; on the debris layer a
    // 1 kg piece and a 2 kg shard.
    [SetUp]
    public void SetUp()
    {
        _simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        _debris = _simulation.Layers.Define("Debris");
        _wall = _simulation.NewBody().Static().WithShape(Crate).At(new WorldPosition(-6, 0, 0)).Add();
        _light = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 0, 0)).WithMass(10).Add();
        _heavy = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(3, 0, 0)).WithMass(500).Add();
        _piece = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(6, 0, 0)).WithMass(1).OnLayer(_debris).Add();
        _shard = _simulation.NewBody().WithShape(Crate).At(new WorldPosition(9, 0, 0)).WithMass(2).OnLayer(_debris).Add();
        _simulation.Step();
    }

    [TearDown]
    public void TearDown() => _simulation.Dispose();

    [Test]
    public void Orders_WriteTheFoundInTheirOrder()
    {
        var byMass = Ordered(_simulation.Find().Dynamic().OrderBy(static body => body.Mass));
        var byMassDown = Ordered(_simulation.Find().Dynamic().OrderByDescending(static body => body.Mass));
        var byLayerThenMass = Ordered(_simulation.Find().Dynamic().OrderByDescending(static body => body.Layer)
            .ThenBy(static body => body.Mass));
        var nearest = Ordered(_simulation.Find().NearestTo(new WorldPosition(5, 0, 0)));

        Assert.Multiple(() =>
        {
            Assert.That(byMass, Is.EqualTo(new[] { _piece, _shard, _light, _heavy }));
            Assert.That(byMassDown, Is.EqualTo(new[] { _heavy, _light, _shard, _piece }));
            Assert.That(byLayerThenMass, Is.EqualTo(new[] { _piece, _shard, _light, _heavy }));
            Assert.That(nearest, Is.EqualTo(new[] { _piece, _heavy, _shard, _light, _wall }));
        });
    }

    // More found than the buffer holds: the first in the order are kept, wherever they come in the walk.
    [Test]
    public void AnOrderInABufferTooSmall_KeepsTheFirstInTheOrder()
    {
        Span<BodyId> two = stackalloc BodyId[2];
        var count = _simulation.Find().OrderByDescending(static body => body.Mass).Into(two);
        var kept = two[..count].ToArray();
        var lightest = _simulation.Find().Dynamic().OrderBy(static body => body.Mass).First();

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(new[] { _heavy, _light }));
            Assert.That(lightest, Is.EqualTo(_piece));
        });
    }

    [Test]
    public void SkipAndTake_KeepTheirStretch_OfTheWalkAndOfAnOrder()
    {
        var walked = _simulation.Find().Count();
        var taken = _simulation.Find().Take(2).Count();
        var skipped = _simulation.Find().Skip(4).Count();
        var middle = Ordered(_simulation.Find().Dynamic().OrderBy(static body => body.Mass).Skip(1).Take(2));
        var past = Ordered(_simulation.Find().OrderBy(static body => body.Mass).Skip(10));

        Assert.Multiple(() =>
        {
            Assert.That(walked, Is.EqualTo(5));
            Assert.That(taken, Is.EqualTo(2));
            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(middle, Is.EqualTo(new[] { _shard, _light }));
            Assert.That(past, Is.Empty);
        });
    }

    // Skipped bodies take no room of the caller's: the first past them, and as many past them as the buffer holds.
    [Test]
    public void SkippedBodies_TakeNoRoomOfTheCallers()
    {
        var second = _simulation.Find().Dynamic().OrderBy(static body => body.Mass).Skip(1).First();
        Span<float> two = stackalloc float[2];
        var count = _simulation.Find().Dynamic().OrderBy(static body => body.Mass).Skip(3).Select(static body => body.Mass)
            .Into(two);
        var heaviest = two[0];
        Span<BodyId> one = stackalloc BodyId[1];
        var pastTwo = _simulation.Find().Dynamic().OrderBy(static body => body.Mass).Skip(2).Into(one);
        var pastTwoBody = one[0];
        var pastAll = _simulation.Find().Skip(int.MaxValue).Skip(1).Count();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(_shard));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(heaviest, Is.EqualTo(500).Within(1e-3f));
            Assert.That(pastTwo, Is.EqualTo(1));
            Assert.That(pastTwoBody, Is.EqualTo(_light));
            Assert.That(pastAll, Is.Zero);
        });
    }

    // Conditions come before Skip and Take: told after, they would be read as before, which LINQ does not.
    [Test]
    public void AConditionAfterSkipOrTake_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().Take(2).Dynamic().Count());
            Assert.Throws<InvalidOperationException>(() => _simulation.Find().Skip(1).Where(static body => true).Count());
        });
    }

    // Grouped by layer, the groups come as their first body does in the order told, each a slice of the buffer in that
    // order; a key with no order of its own groups too, and a group walked after its body is taken out is refused.
    [Test]
    public void Groups_AreSlicesOfTheBuffer_InTheOrderTheirFirstBodyComes()
    {
        Span<BodyId> byData = stackalloc BodyId[8];
        var dataGroups = _simulation.Find().GroupBy(static body => body.Pose.Rotation).Into(byData).Count;
        Span<BodyId> stale = stackalloc BodyId[8];
        var staleGroups = _simulation.Find().GroupBy(static body => body.Kind).Into(stale);
        _simulation.RemoveBody(_wall);
        var staleRefused = false;
        try
        {
            _ = staleGroups.Count;
        }
        catch (InvalidOperationException)
        {
            staleRefused = true;
        }

        Assert.Multiple(() =>
        {
            Assert.That(dataGroups, Is.EqualTo(1), "none turned: by a quaternion, which has no order");
            Assert.That(staleRefused, Is.True);
        });
    }

    [Test]
    public void Groups_AreSlicesOfTheBuffer_ByTheOrderTold()
    {
        Span<BodyId> buffer = stackalloc BodyId[8];
        var groups = new List<(Layer Key, BodyId[] Bodies)>();
        foreach (var group in _simulation.Find().Dynamic().OrderByDescending(static body => body.Mass)
                     .GroupBy(static body => body.Layer).Into(buffer))
            groups.Add((group.Key, group.Bodies.ToArray()));

        Assert.Multiple(() =>
        {
            Assert.That(groups.Select(group => group.Key), Is.EqualTo(new[] { _simulation.Layers.Default, _debris }));
            Assert.That(groups[0].Bodies, Is.EqualTo(new[] { _heavy, _light }));
            Assert.That(groups[1].Bodies, Is.EqualTo(new[] { _shard, _piece }));
            Assert.Throws<InvalidOperationException>(() =>
                _simulation.Find().OrderBy(static body => body.Mass).Take(2).GroupBy(static body => body.Layer));
        });
    }

    [Test]
    public void TheQuerySyntax_ReadsAsTheOperators()
    {
        Span<float> masses = stackalloc float[8];
        var heavyFirst =
            from body in _simulation.Find().Dynamic()
            where body.Layer == default
            orderby body.Mass descending
            select body.Mass;
        var massCount = heavyFirst.Into(masses);
        var massesFound = masses[..massCount].ToArray();

        Span<BodyId> buffer = stackalloc BodyId[8];
        var kinds = new List<(BodyKind Kind, int Count)>();
        foreach (var group in (from body in _simulation.Find() group body by body.Kind).Into(buffer))
            kinds.Add((group.Key, group.Count));

        var debrisNames = new List<string>();
        foreach (var name in from body in _simulation.Find() where body.Layer == _debris select body.Id.ToString())
            debrisNames.Add(name);

        Assert.Multiple(() =>
        {
            Assert.That(massesFound, Is.EqualTo(new[] { 500f, 10f }).Within(1e-3f));
            Assert.That(kinds, Is.EquivalentTo(new[] { (BodyKind.Static, 1), (BodyKind.Dynamic, 4) }));
            Assert.That(debrisNames, Is.EquivalentTo(new[] { _piece.ToString(), _shard.ToString() }));
        });
    }

    // More values than a buffer on the stack holds: the bodies' room comes from the shared pool, the order the same.
    [Test]
    public void ALargeOrderedSelection_KeepsItsOrder()
    {
        for (var index = 0; index < 300; index++)
            _simulation.NewBody().WithShape(Crate).At(new WorldPosition(index * 2, 10, 0)).WithMass(300 - index).Add();

        _simulation.Step();
        var masses = new float[400];
        var count = _simulation.Find().Dynamic().OrderBy(static body => body.Mass).Select(static body => body.Mass)
            .Into(masses);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(304));
            Assert.That(masses[..count], Is.Ordered);
            Assert.That(masses[0], Is.EqualTo(1).Within(1e-4f));
            Assert.That(masses[count - 1], Is.EqualTo(500).Within(1e-3f));
        });
    }

    [Test]
    public void Aggregates_SumAndCompareWhatTheBodiesGive()
    {
        var found = _simulation.Find().Dynamic();
        var sum = found.Sum(static body => body.Mass);
        var least = found.Min(static body => body.Mass);
        var most = found.Max(static body => body.Mass);
        var average = found.Average(static body => body.Mass);
        var allMoving = found.All(static body => body.Kind == BodyKind.Dynamic);
        var noneHeavier = _simulation.Find().Heavier(than: 1000);
        var (noneSum, noneMax, noneAll) = (noneHeavier.Sum(static body => body.Mass),
            noneHeavier.Max(static body => body.Mass), noneHeavier.All(static body => false));

        Assert.Multiple(() =>
        {
            Assert.That(sum, Is.EqualTo(513).Within(1e-3));
            Assert.That(least, Is.EqualTo(1).Within(1e-4));
            Assert.That(most, Is.EqualTo(500).Within(1e-3));
            Assert.That(average, Is.EqualTo(128.25).Within(1e-3));
            Assert.That(allMoving, Is.True);
            Assert.That(noneSum, Is.Zero);
            Assert.That(noneMax, Is.NaN);
            Assert.That(noneAll, Is.True);
        });
    }

    [Test]
    public void ASelection_GivesWhatEachBodyGives_AndEachValueOnceWhenDistinct()
    {
        Span<BodyKind> kinds = stackalloc BodyKind[8];
        var all = _simulation.Find().Select(static body => body.Kind).Into(kinds);
        var distinct = _simulation.Find().Select(static body => body.Kind).Distinct().Into(kinds);
        var distinctKinds = kinds[..distinct].ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(5));
            Assert.That(distinctKinds, Is.EquivalentTo(new[] { BodyKind.Static, BodyKind.Dynamic }));
        });
    }

    // Joined with a span of the caller's own by id - and with the touches told, by the query syntax.
    [Test]
    public void AJoin_MeetsEachBodyWithItsItems()
    {
        ReadOnlySpan<(BodyId Body, string Label)> labels =
        [
            (_light, "light"), (_heavy, "heavy"), (_heavy, "big"), (_wall, "wall")
        ];
        var met = new List<string>();
        foreach (var label in _simulation.Find().Dynamic().Join(labels, static body => body.Id, static item => item.Body,
                     static (_, item) => item.Label))
            met.Add(label);

        _simulation.SetPose(_light, new Pose(new WorldPosition(2, 0, 0)));
        _simulation.Step();
        var touched = new List<BodyId>();
        foreach (var other in from body in _simulation.Find()
                              join touch in _simulation.Contacts on body.Id equals touch.First
                              select touch.Second)
            touched.Add(other);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.EquivalentTo(new[] { "light", "heavy", "big" }));
            Assert.That(touched, Has.Count.EqualTo(1), "the crate put against the heavy one");
        });
    }

    // Orders, groups, projections and joins over the caller's buffers, with lambdas that take in nothing, allocate
    // nothing once the shared pool has what it lends.
    [Test]
    public void TheOperators_AllocateNothing()
    {
        void Frame()
        {
            Span<BodyId> buffer = stackalloc BodyId[8];
            Span<float> masses = stackalloc float[8];
            _simulation.Find().Dynamic().OrderByDescending(static body => body.Mass).ThenBy(static body => body.Layer)
                .Skip(1).Take(2).Into(buffer);
            foreach (var group in _simulation.Find().GroupBy(static body => body.Layer).Into(buffer))
                _ = group.Count;

            (from body in _simulation.Find() orderby body.Mass select body.Mass).Into(masses);
            _simulation.Find().Select(static body => body.Kind).Distinct().Into(stackalloc BodyKind[4]);
            _simulation.Find().Sum(static body => body.Mass);
            foreach (var _ in _simulation.Find().Join(_simulation.Contacts, static body => body.Id,
                         static touch => touch.First, static (body, touch) => touch.Second))
            {
            }

            _simulation.Step();
        }

        for (var frame = 0; frame < 50; frame++)
            Frame();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 200; frame++)
            Frame();

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    private static BodyId[] Ordered<TFilter, TOrder>(OrderedBodies<TFilter, TOrder> ordered)
        where TFilter : IBodyFilter
        where TOrder : IBodyOrder
    {
        Span<BodyId> buffer = stackalloc BodyId[16];
        return buffer[..ordered.Into(buffer)].ToArray();
    }
}
