namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a run of the stepper tells of a body or a joint, besides where it stands.</summary>
internal enum NewsKind
{
    /// <summary>The core took it.</summary>
    Taken,

    /// <summary>The core would not take it; the reason given.</summary>
    Refused,

    /// <summary>The core would not do a change asked of it; the reason given.</summary>
    ChangeRefused,

    /// <summary>A body's new shape was taken: its parts' own go with it.</summary>
    ShapeTaken,

    /// <summary>A body's new layer was taken.</summary>
    LayerTaken,

    /// <summary>A body's new material was taken.</summary>
    MaterialTaken,

    /// <summary>The kind a body has once a kind asked of it was done, taken or not.</summary>
    KindSettled
}
