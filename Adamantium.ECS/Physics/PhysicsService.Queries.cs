using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

public sealed partial class PhysicsService
{
    private const int StackHits = 64;

    /// <inheritdoc/>
    public ReadOnlySpan<EntityContactEventArgs> Contacts => CollectionsMarshal.AsSpan(_contacts);

    /// <inheritdoc/>
    public BodyFinder<AnyBody> Find() => RequireGame().Find().AsReadOnly();

    /// <inheritdoc/>
    public Layer FindLayer(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return RequireGame().Layers.TryFind(name, out var layer)
            ? layer
            : throw new ArgumentException($"The game's physics has no layer named \"{name}\".", nameof(name));
    }

    /// <inheritdoc/>
    public bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out EntityRayHit hit,
        in EntityQueryFilter filter = default)
    {
        hit = default;
        if (_simulation == null || !_simulation.RayCast(origin, direction, maxDistance, out var met, ToQueryFilter(filter)))
            return false;

        hit = new EntityRayHit(EntityOf(met.Body), ColliderOf(met.Body, met.Part), met.Point, met.Normal, met.Distance,
            _simulation.SurfaceOf(met.Body, met.Part));
        return true;
    }

    /// <inheritdoc/>
    public int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<EntityRayHit> hits,
        in EntityQueryFilter filter = default)
    {
        if (_simulation == null)
            return 0;

        RayHit[] rented = null;
        var met = hits.Length <= StackHits
            ? stackalloc RayHit[hits.Length]
            : (rented = ArrayPool<RayHit>.Shared.Rent(hits.Length)).AsSpan(0, hits.Length);
        try
        {
            var count = _simulation.RayCast(origin, direction, maxDistance, met, ToQueryFilter(filter));
            for (var index = 0; index < count; index++)
            {
                ref readonly var one = ref met[index];
                hits[index] = new EntityRayHit(EntityOf(one.Body), ColliderOf(one.Body, one.Part), one.Point, one.Normal,
                    one.Distance, _simulation.SurfaceOf(one.Body, one.Part));
            }

            return count;
        }
        finally
        {
            if (rented != null)
                ArrayPool<RayHit>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out EntityShapeHit hit,
        in EntityQueryFilter filter = default)
    {
        hit = default;
        if (_simulation == null || !_simulation.CastShape(shape, pose, translation, out var met, ToQueryFilter(filter)))
            return false;

        hit = new EntityShapeHit(EntityOf(met.Body), ColliderOf(met.Body, met.Part), met.Point, met.Normal, met.Distance,
            _simulation.SurfaceOf(met.Body, met.Part));
        return true;
    }

    /// <inheritdoc/>
    public int Overlap(CollisionShape shape, in Pose pose, Span<Entity> entities, in EntityQueryFilter filter = default)
    {
        if (_simulation == null)
            return 0;

        BodyId[] rented = null;
        var met = entities.Length <= StackHits
            ? stackalloc BodyId[entities.Length]
            : (rented = ArrayPool<BodyId>.Shared.Rent(entities.Length)).AsSpan(0, entities.Length);
        try
        {
            var count = _simulation.Overlap(shape, pose, met, ToQueryFilter(filter));
            for (var index = 0; index < count; index++)
            {
                entities[index] = EntityOf(met[index]);
            }

            return count;
        }
        finally
        {
            if (rented != null)
                ArrayPool<BodyId>.Shared.Return(rented);
        }
    }

    private void TellContacts()
    {
        _contacts.Clear();
        foreach (ref readonly var contact in _simulation.Contacts)
        {
            _contacts.Add(new EntityContactEventArgs(EntityOf(contact.First), contact.FirstPartData as Collider,
                EntityOf(contact.Second), contact.SecondPartData as Collider, contact));
        }
    }

    private QueryFilter ToQueryFilter(in EntityQueryFilter filter) => new()
    {
        Except = _mainProcessor.FindMadeBody(filter.Except),
        Layers = filter.Layers,
        MeetsTriggers = filter.MeetsTriggers
    };

    private Entity EntityOf(BodyId body) => _simulation.DataOf(body) as Entity;

    private Collider ColliderOf(BodyId body, int part) => _simulation.DataOf(body, part) as Collider;

    private PhysicsSimulation RequireGame() =>
        _simulation ?? throw new InvalidOperationException("No game plays: the physics moves nothing.");
}
