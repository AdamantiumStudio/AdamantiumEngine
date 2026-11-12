using System;
using Adamantium.ECS;

namespace Adamantium.Multiverse;

/// <summary>What wakes a universe that draws only when something changes: it watches what the universe draws from and
/// asks the universe for frames. One for each universe; from any thread.</summary>
public interface IUniverseWatcher
{
    /// <summary>Watches <paramref name="watched"/>: each change it raises asks for frames, until
    /// <see cref="ReleaseWatch"/>.</summary>
    void Watch(INotifyUniverse watched);

    /// <summary>Stops watching <paramref name="watched"/>.</summary>
    void ReleaseWatch(INotifyUniverse watched);

    /// <summary>Asks for one frame.</summary>
    void Watch();

    /// <summary>Asks for the next <paramref name="frames"/> frames.</summary>
    void Watch(int frames);

    /// <summary>Asks for frames for <paramref name="duration"/>.</summary>
    void Watch(TimeSpan duration);

    /// <summary>Asks for a frame of <paramref name="output"/> alone - what changed is that output's own - without the
    /// other outputs drawn again.</summary>
    void Invalidate(UniverseOutput output);
}
