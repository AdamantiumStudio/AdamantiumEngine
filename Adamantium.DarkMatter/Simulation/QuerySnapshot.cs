using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>The world as the caller last read it, for queries while the simulation steps on its own thread: each body
/// a static copy of the same shape, standing where the last run read put it. Touched on the caller's thread only.
/// </summary>
internal sealed class QuerySnapshot
{
    private const int NoSlot = -1;

    private readonly PhysicsWorld _world = new();
    private readonly List<BodyHandle> _handleOfSlot = [];
    private readonly List<int> _generationOfSlot = [];
    private readonly List<CollisionShape> _shapeOfSlot = [];
    // The slot of the copy the snapshot keeps at each of its indices.
    private readonly List<int> _slotOfCopy = [];

    public PhysicsWorld World => _world;

    /// <summary>Puts the body in <paramref name="slot"/> at <paramref name="pose"/> on <paramref name="layer"/>, a copy
    /// of <paramref name="shape"/> and its parts' own; a copy there already is moved, or made anew for a shape not its
    /// own.</summary>
    public void Place(int slot, int generation, CollisionShape shape, PartTraits[] partTraits, Layer layer,
        in Pose pose)
    {
        while (_handleOfSlot.Count <= slot)
        {
            _handleOfSlot.Add(default);
            _generationOfSlot.Add(0);
            _shapeOfSlot.Add(null);
        }

        var handle = _handleOfSlot[slot];
        if (handle.IsValid && _generationOfSlot[slot] == generation && _shapeOfSlot[slot] == shape)
        {
            _world.SetPose(handle, pose);
            _world.SetLayer(handle, layer);
            return;
        }

        Remove(slot);
        handle = _world.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = shape, PartTraits = partTraits, Layer = layer, Position = pose.Position,
            Rotation = pose.Rotation
        });
        while (_slotOfCopy.Count <= handle.Index)
        {
            _slotOfCopy.Add(NoSlot);
        }

        _slotOfCopy[handle.Index] = slot;
        _handleOfSlot[slot] = handle;
        _generationOfSlot[slot] = generation;
        _shapeOfSlot[slot] = shape;
    }

    /// <summary>Puts the copy of the body in <paramref name="slot"/> on <paramref name="layer"/>, if there is one of
    /// its generation.</summary>
    public void SetLayer(int slot, int generation, Layer layer)
    {
        if (slot < _handleOfSlot.Count && _handleOfSlot[slot].IsValid && _generationOfSlot[slot] == generation)
            _world.SetLayer(_handleOfSlot[slot], layer);
    }

    /// <summary>Moves the copy of the body in <paramref name="slot"/>, if there is one of its generation.</summary>
    public void Move(int slot, int generation, in Pose pose)
    {
        if (slot < _handleOfSlot.Count && _handleOfSlot[slot].IsValid && _generationOfSlot[slot] == generation)
            _world.SetPose(_handleOfSlot[slot], pose);
    }

    public void Remove(int slot)
    {
        if (slot >= _handleOfSlot.Count || !_handleOfSlot[slot].IsValid)
            return;

        var handle = _handleOfSlot[slot];
        _slotOfCopy[handle.Index] = NoSlot;
        _world.Remove(handle);
        _handleOfSlot[slot] = default;
        _shapeOfSlot[slot] = null;
    }

    /// <summary>The copy of the body in <paramref name="slot"/>; none for one not here.</summary>
    public BodyHandle HandleOf(int slot) => slot >= 0 && slot < _handleOfSlot.Count ? _handleOfSlot[slot] : default;

    /// <summary>The body a copy stands for.</summary>
    public BodyId IdOf(BodyHandle copy)
    {
        var slot = _slotOfCopy[copy.Index];
        return new BodyId(slot, _generationOfSlot[slot]);
    }
}
