using System;

namespace Adamantium.MVVM;

/// <summary>One property or field of a type, read and written by name - through generated code where a source
/// generator described the type (see <see cref="PropertyAccessors"/>), through reflection otherwise.</summary>
public sealed class PropertyAccessor
{
    /// <summary>Made by generated code; <paramref name="get"/> or <paramref name="set"/> is null when the value cannot
    /// be read or written.</summary>
    public PropertyAccessor(string name, Type valueType, Func<object, object> get, Action<object, object> set)
    {
        Name = name;
        ValueType = valueType;
        Getter = get;
        Setter = set;
    }

    public string Name { get; }

    /// <summary>The declared type of the value.</summary>
    public Type ValueType { get; }

    public bool CanRead => Getter != null;

    /// <summary>False when there is no setter, or one that is not public.</summary>
    public bool CanWrite => Setter != null;

    internal Func<object, object> Getter { get; }

    internal Action<object, object> Setter { get; }

    /// <summary>The value <paramref name="target"/> holds; throws when the value cannot be read.</summary>
    public object GetValue(object target)
    {
        if (Getter == null)
        {
            throw new InvalidOperationException($"'{Name}' cannot be read.");
        }

        return Getter(target);
    }

    /// <summary>Writes <paramref name="value"/> into <paramref name="target"/>; null writes a value type's default.
    /// Throws when the value cannot be written.</summary>
    public void SetValue(object target, object value)
    {
        if (Setter == null)
        {
            throw new InvalidOperationException($"'{Name}' cannot be written.");
        }

        Setter(target, value);
    }
}
