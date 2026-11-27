using System;

namespace Adamantium.DarkMatter;

/// <summary>
/// A search of a <see cref="PhysicsSimulation"/>'s bodies, told link by link on the caller's stack: ready conditions,
/// then any <c>Where</c>, each keeping fewer. It walks the bodies the last step read, as they stood then, and ends in
/// an answer - <see cref="Into"/>, <see cref="Count"/>, <see cref="Any"/>, <see cref="First"/>, an aggregate, a
/// <c>foreach</c> - in actions on every body found, each the simulation's own, or goes on as an order, a grouping, a
/// projection or a join. Nothing is allocated, but by a lambda's closure. It walks in no order kept; an order is told by
/// <see cref="OrderBy{TKey}(Func{BodyView, TKey})"/> or <see cref="NearestTo"/>.
/// </summary>
public readonly ref struct BodyFinder<TFilter>
    where TFilter : IBodyFilter
{
    private readonly PhysicsSimulation _simulation;
    private readonly FindConditions _conditions;
    private readonly TFilter _filter;
    private readonly int _skip;
    private readonly int _take;

    internal BodyFinder(PhysicsSimulation simulation, FindConditions conditions, TFilter filter, int skip = 0,
        int take = int.MaxValue)
    {
        _simulation = simulation;
        _conditions = conditions;
        _filter = filter;
        _skip = skip;
        _take = take;
    }

    internal PhysicsSimulation Simulation => _simulation;

    /// <summary>Keeps dynamic bodies; told with another kind, either.</summary>
    public BodyFinder<TFilter> Dynamic() => With(_conditions.WithKind(BodyKind.Dynamic));

    public BodyFinder<TFilter> Kinematic() => With(_conditions.WithKind(BodyKind.Kinematic));

    public BodyFinder<TFilter> Static() => With(_conditions.WithKind(BodyKind.Static));

    public BodyFinder<TFilter> Awake() => With(_conditions with { AwakeOnly = true });

    public BodyFinder<TFilter> Asleep() => With(_conditions with { AsleepOnly = true });

    public BodyFinder<TFilter> OnLayers(LayerMask layers) =>
        With(_conditions with { PassedLayers = _conditions.PassedLayers | ~layers.Bits });

    /// <summary>Passes <paramref name="body"/> by: one body; told again, the new one.</summary>
    public BodyFinder<TFilter> Except(BodyId body) => With(_conditions with { Except = body });

    /// <summary>Keeps bodies weighing more than <paramref name="than"/> kilograms; told again, the heavier bound.
    /// </summary>
    public BodyFinder<TFilter> Heavier(float than)
    {
        if (!float.IsFinite(than))
            throw new ArgumentOutOfRangeException(nameof(than), than, "The mass must be finite.");

        var least = _conditions.HasLeastMass ? MathF.Max(_conditions.LeastMass, than) : than;
        return With(_conditions with { HasLeastMass = true, LeastMass = least });
    }

    /// <summary>Keeps bodies whose center of mass is within <paramref name="area"/>; told again, the new area counts.
    /// </summary>
    public BodyFinder<TFilter> Within(in BoundingBox<double> area)
    {
        FindConditions.RequireArea(area, nameof(area));
        return With(_conditions with { HasBox = true, Box = area, HasSphere = false });
    }

    /// <summary>Keeps bodies whose center of mass is within <paramref name="area"/>; told again, the new area counts.
    /// </summary>
    public BodyFinder<TFilter> Within(in BoundingSphere<double> area)
    {
        FindConditions.RequireArea(area, nameof(area));
        return With(_conditions with { HasSphere = true, Sphere = area, HasBox = false });
    }

    /// <summary>Keeps bodies <paramref name="filter"/> keeps: a struct of the caller's own, called straight.</summary>
    public BodyFinder<BothFilters<TFilter, TNext>> Where<TNext>(TNext filter)
        where TNext : IBodyFilter
    {
        RequireNoStretch();
        return new BodyFinder<BothFilters<TFilter, TNext>>(_simulation, _conditions,
            new BothFilters<TFilter, TNext>(_filter, filter));
    }

    /// <summary>Keeps bodies <paramref name="predicate"/> keeps, looking at <paramref name="data"/>: a static lambda,
    /// with no closure.</summary>
    public BodyFinder<BothFilters<TFilter, DataFilter<TData>>> Where<TData>(TData data,
        Func<BodyView, TData, bool> predicate) =>
        Where(new DataFilter<TData>(data, predicate));

    /// <summary>Keeps bodies <paramref name="predicate"/> keeps: what <c>where</c> of the query syntax calls.</summary>
    public BodyFinder<BothFilters<TFilter, DelegateFilter>> Where(Func<BodyView, bool> predicate) =>
        Where(new DelegateFilter(predicate));

    /// <summary>Passes by the first <paramref name="count"/> bodies found; told again, past those too. Conditions come
    /// before it.</summary>
    public BodyFinder<TFilter> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new BodyFinder<TFilter>(_simulation, _conditions, _filter, AddStretch(_skip, count),
            _take == int.MaxValue ? int.MaxValue : Math.Max(0, _take - count));
    }

    /// <summary>Keeps at most <paramref name="count"/> bodies found; told again, the fewer. Conditions come before it.
    /// </summary>
    public BodyFinder<TFilter> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new BodyFinder<TFilter>(_simulation, _conditions, _filter, _skip, Math.Min(_take, count));
    }

    /// <summary>Answers nearest to <paramref name="point"/> first, by the center of mass.</summary>
    public OrderedBodies<TFilter, NearestOrder> NearestTo(WorldPosition point)
    {
        if (!point.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(point), point, "The point must be finite.");

        return Order(new NearestOrder(point));
    }

    /// <summary>Answers by a key of each body, the least first: <c>orderby</c> of the query syntax.</summary>
    public OrderedBodies<TFilter, KeyOrder<TKey>> OrderBy<TKey>(Func<BodyView, TKey> key) =>
        Order(new KeyOrder<TKey>(key, false));

    /// <summary>Answers by a key of each body, the greatest first: <c>orderby … descending</c>.</summary>
    public OrderedBodies<TFilter, KeyOrder<TKey>> OrderByDescending<TKey>(Func<BodyView, TKey> key) =>
        Order(new KeyOrder<TKey>(key, true));

    /// <summary>Answers in an order of the caller's own: a struct, called straight.</summary>
    public OrderedBodies<TFilter, TOrder> OrderBy<TOrder>(TOrder order)
        where TOrder : IBodyOrder =>
        Order(order);

    /// <summary>Groups the bodies found by a key of each: <c>group … by</c> of the query syntax, the groups slices of
    /// a buffer, in the order their first body comes.</summary>
    public BodyGroups<TFilter, NoOrder, TKey> GroupBy<TKey>(Func<BodyView, TKey> key) => new(this, default, key);

    /// <summary>What each body found gives: <c>select</c> of the query syntax.</summary>
    public BodySelection<TFilter, TResult> Select<TResult>(Func<BodyView, TResult> selector) => new(this, selector);

    /// <summary>Each body found met with each item of <paramref name="inner"/> whose key is its own: <c>join … on …
    /// equals …</c> of the query syntax - the touches told, say.</summary>
    public BodyJoin<TFilter, TInner, TKey, TResult> Join<TInner, TKey, TResult>(ReadOnlySpan<TInner> inner,
        Func<BodyView, TKey> bodyKey, Func<TInner, TKey> innerKey, Func<BodyView, TInner, TResult> result) =>
        new(this, inner, bodyKey, innerKey, result);

    public BodyFinderEnumerator<TFilter> GetEnumerator() => new(_simulation, _conditions, _filter, _skip, _take);

    /// <summary>Writes the bodies found into <paramref name="bodies"/>, as many as fit, and gives how many it wrote.
    /// </summary>
    public int Into(Span<BodyId> bodies)
    {
        var written = 0;
        foreach (var body in this)
        {
            if (written == bodies.Length)
                break;

            bodies[written++] = body.Id;
        }

        return written;
    }

    public int Count()
    {
        var count = 0;
        foreach (var _ in this)
            count++;

        return count;
    }

    public bool Any()
    {
        foreach (var _ in this)
            return true;

        return false;
    }

    /// <summary>Whether <paramref name="predicate"/> keeps every body found; true for none found.</summary>
    public bool All(Func<BodyView, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var body in this)
        {
            if (!predicate(body))
                return false;
        }

        return true;
    }

    /// <summary>A body found; none when nothing is found.</summary>
    public BodyId First()
    {
        foreach (var body in this)
            return body.Id;

        return BodyId.None;
    }

    /// <summary>What <paramref name="value"/> gives of the bodies found, added; zero for none found.</summary>
    public double Sum(Func<BodyView, double> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sum = 0.0;
        foreach (var body in this)
            sum += value(body);

        return sum;
    }

    /// <summary>The least <paramref name="value"/> gives of the bodies found - not a number if any gives one; not a
    /// number for none found.</summary>
    public double Min(Func<BodyView, double> value) => Aggregate(value, true);

    /// <summary>The greatest <paramref name="value"/> gives of the bodies found, past those that give not a number; not
    /// a number for none found.</summary>
    public double Max(Func<BodyView, double> value) => Aggregate(value, false);

    /// <summary>What <paramref name="value"/> gives of the bodies found, on average; not a number for none found.
    /// </summary>
    public double Average(Func<BodyView, double> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sum = 0.0;
        var count = 0;
        foreach (var body in this)
        {
            sum += value(body);
            count++;
        }

        return count > 0 ? sum / count : double.NaN;
    }

    /// <summary>The same search, only reading: its actions are refused - for bodies another keeps, such as a game's
    /// physics that keeps them as its entities ask.</summary>
    public BodyFinder<TFilter> AsReadOnly() =>
        new(_simulation, _conditions with { IsReadOnly = true }, _filter, _skip, _take);

    /// <summary>Wakes every body found, and what sleeps with each.</summary>
    public BodyFinder<TFilter> WakeUp()
    {
        RequireActions();
        foreach (var body in this)
            _simulation.WakeUp(body.Id);

        return this;
    }

    /// <summary>Strikes every dynamic body found at its center of mass; the others take no impulse.</summary>
    public BodyFinder<TFilter> ApplyImpulse(Vector3F impulse)
    {
        RequireActions();
        foreach (var body in this)
        {
            if (body.KindAsked == BodyKind.Dynamic)
                _simulation.ApplyImpulse(body.Id, impulse);
        }

        return this;
    }

    /// <summary>Sets the velocity of every body found that moves; a static one keeps still.</summary>
    public BodyFinder<TFilter> SetLinearVelocity(Vector3F velocity)
    {
        RequireActions();
        foreach (var body in this)
        {
            if (body.KindAsked != BodyKind.Static)
                _simulation.SetLinearVelocity(body.Id, velocity);
        }

        return this;
    }

    /// <summary>Sets the spin of every body found that moves; a static one keeps still.</summary>
    public BodyFinder<TFilter> SetAngularVelocity(Vector3F velocity)
    {
        RequireActions();
        foreach (var body in this)
        {
            if (body.KindAsked != BodyKind.Static)
                _simulation.SetAngularVelocity(body.Id, velocity);
        }

        return this;
    }

    public BodyFinder<TFilter> SetLayer(Layer layer)
    {
        RequireActions();
        foreach (var body in this)
            _simulation.SetLayer(body.Id, layer);

        return this;
    }

    public BodyFinder<TFilter> SetMaterial(in PhysicsMaterial material)
    {
        RequireActions();
        foreach (var body in this)
            _simulation.SetMaterial(body.Id, material);

        return this;
    }

    /// <summary>Takes every body found out, their joints with them; the chain ends here.</summary>
    public void RemoveBodies()
    {
        RequireActions();
        foreach (var body in this)
            _simulation.RemoveBody(body.Id);
    }

    private void RequireActions()
    {
        if (_conditions.IsReadOnly)
            throw new InvalidOperationException("This search only reads; its bodies are acted on by their owner.");
    }

    /// <summary>Skips added on, held at the most an int holds.</summary>
    internal static int AddStretch(int skip, int count) => (int)Math.Min((long)skip + count, int.MaxValue);

    private OrderedBodies<TFilter, TOrder> Order<TOrder>(TOrder order)
        where TOrder : IBodyOrder =>
        new(this, order, 0, int.MaxValue);

    private double Aggregate(Func<BodyView, double> value, bool takesLeast)
    {
        ArgumentNullException.ThrowIfNull(value);
        var kept = double.NaN;
        var isFound = false;
        foreach (var body in this)
        {
            var next = value(body);
            if (double.IsNaN(next))
            {
                if (takesLeast)
                    return double.NaN;

                continue;
            }

            if (!isFound || (takesLeast ? next < kept : next > kept))
                kept = next;

            isFound = true;
        }

        return kept;
    }

    private BodyFinder<TFilter> With(FindConditions conditions)
    {
        RequireNoStretch();
        return new BodyFinder<TFilter>(_simulation, conditions, _filter);
    }

    private void RequireNoStretch()
    {
        if (_skip != 0 || _take != int.MaxValue)
            throw new InvalidOperationException("Conditions come before Skip and Take.");
    }
}
