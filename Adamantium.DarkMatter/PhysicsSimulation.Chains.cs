using System;

namespace Adamantium.DarkMatter;

// The chains: thin structs on the caller's stack over the simulation's own methods, each link one of them. And the
// world's actions on what stands in an area, which the chains reach too.
public sealed partial class PhysicsSimulation
{
    private long _walks;

    /// <summary>The actions and readings of <paramref name="body"/> as one chain.</summary>
    public BodyActions Body(BodyId body) => new(this, body);

    /// <summary>A body told link by link, added by <see cref="BodyBuilder.Add"/>.</summary>
    public BodyBuilder NewBody() => new(this, new BodySettings());

    /// <summary>A joint told link by link, added by <see cref="JointBuilder.Add"/>.</summary>
    public JointBuilder NewJoint() => new(this, new JointSettings());

    /// <summary>A query told link by link: its filter, then the ray, cast or overlap that answers it.</summary>
    public QueryBuilder Query() => new(this, default);

    /// <summary>The world's own actions as one chain.</summary>
    public WorldActions World() => new(this);

    /// <summary>The bodies the last step read, found by what they are and where they stand; every body the simulation
    /// took until told otherwise.</summary>
    public BodyFinder<AnyBody> Find() => new(this, default, default);

    // The bodies alive, for a search to walk: a view of one the simulation took, as the last step read it.
    internal int LiveBodyCount => _liveBodies.Count;

    // A number of its own for each walk of a search, so a body given by it is given once.
    internal long StartWalk() => ++_walks;

    // A body a search wrote into a caller's buffer, as the last step left it.
    internal BodyView ViewOf(BodyId body) => new(body, _bodySlots[body.Index]);

    // A body a search wrote into a caller's buffer, walked again later: refused once it was taken out.
    internal BodyView ViewOfKept(BodyId body)
    {
        ThrowIfDisposed();
        var record = _bodySlots[body.Index];
        if (record.Generation != body.Generation || record.Status == PhysicsStatus.Removed)
            throw new InvalidOperationException($"{body} was taken out since it was grouped.");

        return new BodyView(body, record);
    }

    // The live body at index, when it is within the conditions' area - looked at by its center alone - and the
    // simulation took it.
    internal bool TryViewLiveBody(int index, in FindConditions conditions, out BodyView view)
    {
        var id = _liveBodies[index];
        view = default;
        if (!conditions.IsWithin(_centers[id.Index]))
            return false;

        var body = _bodySlots[id.Index];
        view = new BodyView(id, body);
        return body.Status == PhysicsStatus.Active;
    }

    /// <summary>Wakes every body asleep whose center of mass is within <paramref name="area"/>, and what sleeps with
    /// it.</summary>
    public void WakeUp(in BoundingBox<double> area)
    {
        ThrowIfDisposed();
        Find().Asleep().Within(area).WakeUp();
    }

    /// <summary>An explosion: strikes every body dynamic once the actions asked are done whose center of mass the last
    /// step read is within <paramref name="radius"/>
    /// meters of <paramref name="center"/>, away from it, with <paramref name="strength"/> newton seconds at the center,
    /// fading to none at the radius. One at the very center is struck by nothing.</summary>
    public void ApplyRadialImpulse(WorldPosition center, float radius, float strength)
    {
        RequireFinite(center, nameof(center));
        if (!(radius > 0) || !float.IsFinite(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius must be finite, above zero.");

        if (!float.IsFinite(strength))
            throw new ArgumentOutOfRangeException(nameof(strength), strength, "The strength must be finite.");

        // Dynamic as the actions asked will leave it: one asked to stop being so takes no impulse.
        foreach (var body in Find().Within(new BoundingSphere<double>(center.ToVector(), radius)))
        {
            var away = body.CenterOfMass.OffsetFrom(center);
            var distance = away.Length();
            if (body.KindAsked == BodyKind.Dynamic && distance > 0)
                ApplyImpulse(body.Id, away / distance * (strength * (1 - distance / radius)));
        }
    }
}
