using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter;

// Layers: which of them collide, and a body's. A pair whose layers stop colliding lets go at once; one whose layers
// start to is found with the next step.
internal sealed partial class PhysicsWorld
{
    private readonly LayerCollisions _layerCollisions = new();

    /// <inheritdoc/>
    public void SetLayer(BodyHandle body, Layer layer) => TakeLayer(Resolve(body), layer.Index);

    /// <inheritdoc/>
    public void SetLayersCollide(Layer first, Layer second, bool collides)
    {
        if (!_layerCollisions.Set(first.Index, second.Index, collides))
            return;

        if (!collides)
        {
            // From the end, as one let go of takes the last one's place.
            for (var index = _contacts.Count - 1; index >= 0; index--)
            {
                var contact = _contacts[index];
                if (!PartsCollide(contact.FirstPart, contact.SecondPart))
                    LetGo(contact);
            }

            return;
        }

        for (var index = 0; index < _slotCount; index++)
        {
            if (_bodies[index].IsUsed && HasPartOn(index, first.Index, second.Index))
                LookForPairs(index);
        }
    }

    // The body's layer, and its parts' with none of their own: pairs that no longer collide let go, and it looks for
    // those that now do.
    private void TakeLayer(int index, int layer)
    {
        ref var state = ref _bodies[index];
        if (state.Layer == layer)
            return;

        state.Layer = layer;
        var changed = false;
        for (var part = state.FirstPart; part != NoPart; part = _parts[part].Next)
        {
            if (_parts[part].HasOwnLayer)
                continue;

            _parts[part].Layer = layer;
            changed = true;
        }

        if (!changed)
            return;

        // A pair with a surface goes with its pieces: made after it, they stand before it in the list, passed by already.
        var contact = state.Contacts;
        while (contact != null)
        {
            var next = contact.GetNext(index);
            if (contact.Holder == null && !PartsCollide(contact.FirstPart, contact.SecondPart))
                LetGo(contact);

            contact = next;
        }

        LookForPairs(index);
    }

    // A pair let go of: what it held up falls.
    private void LetGo(Contact contact)
    {
        if (contact.IsTouching)
        {
            WakeIsland(contact.First);
            WakeIsland(contact.Second);
        }

        DestroyContact(contact);
    }

    // Awake and placed anew, it finds its pairs again at the next step and wakes the sleepers it then touches.
    private void LookForPairs(int index)
    {
        WakeIsland(index);
        Mark(index, BodyMarks.Moved | BodyMarks.Placed);
    }

    private bool HasPartOn(int index, int first, int second)
    {
        for (var part = _bodies[index].FirstPart; part != NoPart; part = _parts[part].Next)
        {
            var layer = _parts[part].Layer;
            if (layer == first || layer == second)
                return true;
        }

        return false;
    }

    private bool PartsCollide(int first, int second) =>
        _layerCollisions.Collide(_parts[first].Layer, _parts[second].Layer);
}
