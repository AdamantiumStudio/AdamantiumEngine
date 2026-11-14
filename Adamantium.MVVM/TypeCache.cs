using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Adamantium.MVVM;

/// <summary>Values by type that never keep a collectible type alive - one of code loaded to be unloaded again, such as
/// a game's built in an editor: its value goes when the type does. Other types are kept for good.</summary>
internal sealed class TypeCache<TValue> where TValue : class
{
    private readonly ConcurrentDictionary<Type, TValue> _lasting = new();
    private readonly ConditionalWeakTable<Type, TValue> _collectible = new();

    /// <summary>The value kept for <paramref name="type"/>; false when there is none.</summary>
    public bool TryGetValue(Type type, out TValue value) =>
        type.IsCollectible ? _collectible.TryGetValue(type, out value) : _lasting.TryGetValue(type, out value);

    /// <summary>The value kept for <paramref name="type"/>, made by <paramref name="create"/> when there is none.</summary>
    public TValue GetOrAdd(Type type, Func<Type, TValue> create)
    {
        if (!type.IsCollectible)
            return _lasting.GetOrAdd(type, create);

        if (_collectible.TryGetValue(type, out var known))
            return known;

        return _collectible.GetValue(type, key => create(key));
    }

    /// <summary>Keeps <paramref name="value"/> for <paramref name="type"/> in place of any before.</summary>
    public void Set(Type type, TValue value)
    {
        if (type.IsCollectible)
            _collectible.AddOrUpdate(type, value);
        else
            _lasting[type] = value;
    }
}
