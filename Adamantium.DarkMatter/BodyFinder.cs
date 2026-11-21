using System;

namespace Adamantium.DarkMatter;

/// <summary>
/// A search of a <see cref="PhysicsSimulation"/>'s bodies, told link by link on the caller's stack: ready conditions,
/// then any <c>Where</c>, each keeping fewer. It walks the bodies the last step read, as they stood then, and ends in
/// an answer - <see cref="Into"/>, <see cref="Count"/>, <see cref="Any"/>, <see cref="First"/>, a <c>foreach</c> - or in
/// actions on every body found, each the simulation's own. Nothing is allocated, but by a lambda's closure.
/// </summary>
public readonly ref struct BodyFinder<TFilter>
    where TFilter : IBodyFilter
{
    private readonly PhysicsSimulation _simulation;
    private readonly FindConditions _conditions;
    private readonly TFilter _filter;
    private readonly bool _isNearestFirst;
    private readonly WorldPosition _nearestTo;

    internal BodyFinder(PhysicsSimulation simulation, FindConditions conditions, TFilter filter,
        bool isNearestFirst = false, WorldPosition nearestTo = default)
    {
        _simulation = simulation;
        _conditions = conditions;
        _filter = filter;
        _isNearestFirst = isNearestFirst;
        _nearestTo = nearestTo;
    }

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
        where TNext : IBodyFilter =>
        new(_simulation, _conditions, new BothFilters<TFilter, TNext>(_filter, filter), _isNearestFirst, _nearestTo);

    /// <summary>Keeps bodies <paramref name="predicate"/> keeps, looking at <paramref name="data"/>: a static lambda,
    /// with no closure.</summary>
    public BodyFinder<BothFilters<TFilter, DataFilter<TData>>> Where<TData>(TData data,
        Func<BodyView, TData, bool> predicate) =>
        Where(new DataFilter<TData>(data, predicate));

    /// <summary>Keeps bodies <paramref name="predicate"/> keeps: what <c>where</c> of the query syntax calls.</summary>
    public BodyFinder<BothFilters<TFilter, DelegateFilter>> Where(Func<BodyView, bool> predicate) =>
        Where(new DelegateFilter(predicate));

    /// <summary>Makes <see cref="Into"/> and <see cref="First"/> answer nearest to <paramref name="point"/> first, by the
    /// center of mass; a <c>foreach</c>, <see cref="Count"/> and the actions walk in no order.</summary>
    public BodyFinder<TFilter> NearestTo(WorldPosition point)
    {
        if (!point.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(point), point, "The point must be finite.");

        return new BodyFinder<TFilter>(_simulation, _conditions, _filter, true, point);
    }

    public Enumerator GetEnumerator() => new(_simulation, _conditions, _filter);

    /// <summary>Writes the bodies found into <paramref name="bodies"/> - nearest first, the nearest as many as fit, when
    /// told - and gives how many it wrote.</summary>
    public int Into(Span<BodyId> bodies)
    {
        if (!_isNearestFirst)
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

        // Kept nearest first as they come; a kept body's distance is read again rather than held, so a buffer of any
        // size needs nothing more.
        var count = 0;
        if (bodies.IsEmpty)
            return 0;

        foreach (var body in this)
        {
            var distance = DistanceOf(body.CenterOfMass);
            if (count == bodies.Length && distance >= DistanceOf(bodies[count - 1]))
                continue;

            var at = Math.Min(count, bodies.Length - 1);
            while (at > 0 && DistanceOf(bodies[at - 1]) > distance)
            {
                bodies[at] = bodies[at - 1];
                at--;
            }

            bodies[at] = body.Id;
            count = Math.Min(count + 1, bodies.Length);
        }

        return count;
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

    /// <summary>The body found - the nearest, when told; none when nothing is found.</summary>
    public BodyId First()
    {
        Span<BodyId> first = stackalloc BodyId[1];
        return Into(first) == 1 ? first[0] : BodyId.None;
    }

    /// <summary>Wakes every body found, and what sleeps with each.</summary>
    public BodyFinder<TFilter> WakeUp()
    {
        foreach (var body in this)
            _simulation.WakeUp(body.Id);

        return this;
    }

    /// <summary>Strikes every dynamic body found at its center of mass; the others take no impulse.</summary>
    public BodyFinder<TFilter> ApplyImpulse(Vector3F impulse)
    {
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
        foreach (var body in this)
        {
            if (body.KindAsked != BodyKind.Static)
                _simulation.SetAngularVelocity(body.Id, velocity);
        }

        return this;
    }

    public BodyFinder<TFilter> SetLayer(Layer layer)
    {
        foreach (var body in this)
            _simulation.SetLayer(body.Id, layer);

        return this;
    }

    public BodyFinder<TFilter> SetMaterial(in PhysicsMaterial material)
    {
        foreach (var body in this)
            _simulation.SetMaterial(body.Id, material);

        return this;
    }

    /// <summary>Takes every body found out, their joints with them; the chain ends here.</summary>
    public void RemoveBodies()
    {
        foreach (var body in this)
            _simulation.RemoveBody(body.Id);
    }

    private double DistanceOf(WorldPosition center) => (center - _nearestTo).LengthSquared();

    // A kept body's, read from the centers side by side.
    private double DistanceOf(BodyId kept) => DistanceOf(_simulation.CenterOfSlot(kept.Index));

    private BodyFinder<TFilter> With(FindConditions conditions) =>
        new(_simulation, conditions, _filter, _isNearestFirst, _nearestTo);

    /// <summary>Walks the bodies found, from the last added, each once: a body taken out on the way, the one walked or
    /// another, leaves the rest walked; a body added on the way is not walked.</summary>
    public ref struct Enumerator
    {
        private readonly PhysicsSimulation _simulation;
        private readonly FindConditions _conditions;
        private readonly TFilter _filter;
        private readonly long _walk;
        private int _index;

        internal Enumerator(PhysicsSimulation simulation, FindConditions conditions, TFilter filter)
        {
            simulation.ThrowIfDisposed();
            _simulation = simulation;
            _conditions = conditions;
            _filter = filter;
            _walk = simulation.StartWalk();
            _index = simulation.LiveBodyCount;
            Current = default;
        }

        public BodyView Current { get; private set; }

        public bool MoveNext()
        {
            _index = Math.Min(_index, _simulation.LiveBodyCount);
            while (--_index >= 0)
            {
                // One walked already and moved here by a removal is passed by.
                if (!_simulation.TryViewLiveBody(_index, _conditions, out var body) || !_conditions.Matches(body)
                    || !_filter.Matches(body) || !body.TakeIntoWalk(_walk))
                    continue;

                Current = body;
                return true;
            }

            return false;
        }
    }
}
