using System.Collections.Generic;
using Adamantium.DarkMatter;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>Holds a rigid body to another's, or to the world, while a game plays: the kind of hold is the component's
/// own. The body is its entity's, or the one above it, as for a collider - several joints of one kind on a body stand on
/// entities of their own under it. Its anchor is a point of its entity; the other body is held where it stands when the
/// game starts.</summary>
[ViewModel]
public abstract partial class Joint : ActivatableComponent
{
    /// <summary>The entity whose rigid body - its own, or the one above it - this one is held to; none holds it to the
    /// world.</summary>
    [Bindable, DoNotClone]
    public partial Entity ConnectedBody { get; set; }

    /// <summary>The joint's point, in its entity's own space.</summary>
    [Bindable]
    public partial Vector3F Anchor { get; set; }

    /// <summary>Whether the two bodies still collide with each other; they do not unless told.</summary>
    [Bindable]
    public partial bool CollideConnected { get; set; }

    /// <summary>The kind of hold the physics makes of it.</summary>
    public abstract JointKind Kind { get; }

    // A copy held to an entity copied with it is held to that entity's copy; one held to an entity left as it is, to
    // that same entity.
    public override void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies)
    {
        base.CloneReferences(clone, copies);
        var copy = (Joint)clone;
        copy.ConnectedBody = ConnectedBody != null && copies.TryGetValue(ConnectedBody, out var copied)
            ? copied
            : ConnectedBody;
    }
}
