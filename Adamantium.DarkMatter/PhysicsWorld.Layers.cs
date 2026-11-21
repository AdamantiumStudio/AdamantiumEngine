using System.Collections.Generic;
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
            LetGoOfPairsApart(_contacts, true);
            LetGoOfPairsApart(_triggerContacts, false);
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

        LetGoOfPairsApart(state.Contacts, index, true);
        LetGoOfPairsApart(state.Triggers, index, false);
        LookForPairs(index);
    }

    // From the end, as one let go of takes the last one's place.
    private void LetGoOfPairsApart(List<Contact> pairs, bool wakes)
    {
        for (var index = pairs.Count - 1; index >= 0; index--)
        {
            var contact = pairs[index];
            if (!PartsCollide(contact.FirstPart, contact.SecondPart))
                LetGo(contact, wakes);
        }
    }

    // A pair with a surface goes with its pieces: made after it, they stand before it in the body's list, passed by
    // already.
    private void LetGoOfPairsApart(Contact first, int index, bool wakes)
    {
        var contact = first;
        while (contact != null)
        {
            var next = contact.GetNext(index);
            if (contact.Holder == null && !PartsCollide(contact.FirstPart, contact.SecondPart))
                LetGo(contact, wakes);

            contact = next;
        }
    }

    // A pair let go of: what it held up falls; a trigger's held up nothing.
    private void LetGo(Contact contact, bool wakes)
    {
        if (wakes && contact.IsTouching)
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
