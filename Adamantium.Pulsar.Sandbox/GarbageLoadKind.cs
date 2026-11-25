namespace Adamantium.Pulsar.Sandbox;

internal enum GarbageLoadKind
{
    None,

    /// <summary>Objects dropped at once.</summary>
    ShortLived,

    /// <summary>A large live graph whose objects are replaced all the time.</summary>
    LiveHeap,

    /// <summary>The same, with a full blocking collection every two seconds - a level loaded.</summary>
    FullCollections,
}
