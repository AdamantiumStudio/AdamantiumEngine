using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a run of the stepper tells of one body or joint, in the order it happened.</summary>
internal struct SimulationNews
{
    public NewsKind Kind;

    /// <summary>Whether it is of a joint, not a body.</summary>
    public bool IsJoint;
    public int Slot;
    public int Generation;
    public string Refusal;

    /// <summary>A shape taken - a body's new one, or its first - and its parts' own.</summary>
    public CollisionShape Shape;
    public object[] PartData;
    public PartTraits[] PartTraits;

    /// <summary>A body's layer taken.</summary>
    public Layer Layer;

    /// <summary>The kind a body settled at, and which of the kinds asked of it that was.</summary>
    public BodyKind BodyKind;
    public int KindAsk;
}
