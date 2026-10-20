using System;

namespace Adamantium.MVVM;

/// <summary>One property or field of a type, read and written through plain delegates - generated code where a source
/// generator described the type (see <see cref="PropertyAccessors"/>), reflection otherwise.</summary>
public sealed class PropertyAccessor
{
    public PropertyAccessor(string name, Type valueType, Func<object, object> get, Action<object, object> set)
    {
        Name = name;
        ValueType = valueType;
        Get = get;
        Set = set;
    }

    public string Name { get; }

    /// <summary>The declared type of the value.</summary>
    public Type ValueType { get; }

    /// <summary>Reads the value from an object of the type; null when it cannot be read.</summary>
    public Func<object, object> Get { get; }

    /// <summary>Writes a value into an object of the type; null when nobody else may write it (no setter, or one that
    /// is not public).</summary>
    public Action<object, object> Set { get; }

    public bool CanWrite => Set != null;
}
