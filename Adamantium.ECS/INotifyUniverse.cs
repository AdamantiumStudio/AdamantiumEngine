using System;

namespace Adamantium.ECS;

/// <summary>Something a universe draws from, telling it that what it shows has changed. Given to the universe's
/// <see cref="IUniverseWatcher"/>, it wakes the universe whenever it raises <see cref="UniverseChanged"/>.</summary>
public interface INotifyUniverse
{
    /// <summary>What the universe shows has changed; from any thread.</summary>
    event EventHandler<UniverseChangedEventArgs> UniverseChanged;
}
