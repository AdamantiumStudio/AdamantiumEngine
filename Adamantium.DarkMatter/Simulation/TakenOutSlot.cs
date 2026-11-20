namespace Adamantium.DarkMatter.Simulation;

/// <summary>A slot of a body or a joint taken out, free once the batch of commands that takes it out was run - so no
/// command of the next one there is run before.</summary>
internal readonly record struct TakenOutSlot(int Slot, bool IsJoint, long Batch);
