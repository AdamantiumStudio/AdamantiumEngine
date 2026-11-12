using Adamantium.ECS;

namespace Adamantium.Multiverse;

/// <summary>The universe's watcher, which also asks for a frame of one output alone - what changed is that output's
/// own - without the other outputs drawn again.</summary>
public interface IUniverseOutputWatcher : IUniverseWatcher
{
    /// <summary>Asks for a frame of <paramref name="output"/> alone; nothing while the universe draws every frame.
    /// </summary>
    void Invalidate(UniverseOutput output);
}
