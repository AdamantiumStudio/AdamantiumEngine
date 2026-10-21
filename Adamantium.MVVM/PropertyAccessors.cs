using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Adamantium.MVVM;

/// <summary>Reaches the properties of objects by name - what bindings and data paths read and write - through plain
/// delegates a source generator wrote where it described the type: the MVVM generator describes the non-generic classes
/// with <see cref="System.ComponentModel.INotifyPropertyChanged"/> or <see cref="ViewModelAttribute"/> (with the
/// properties and commands it generates for them) and every class under a base marked
/// <see cref="DescribePropertiesAttribute"/>. What no description holds - another type, a property another generator
/// adds, a struct's member, an interface's member, a field - is reached through reflection, and
/// <see cref="ReachedThroughReflection"/> says so once per type.</summary>
public static class PropertyAccessors
{
    private static readonly object RegistrationLock = new();
    private static readonly Dictionary<Type, Func<PropertyAccessor[]>[]> Described = new();
    private static readonly ConcurrentDictionary<Type, byte> Reflected = new();
    private static volatile ConcurrentDictionary<Type, PropertyAccessor[]> declared = new();
    private static volatile ConcurrentDictionary<(Type Type, string Name), PropertyAccessor> found = new();
    private static volatile bool stale;

    /// <summary>Told the first time a type is reached through reflection; raised on the thread that looked. What a
    /// subscriber throws does not reach the lookup.</summary>
    public static event Action<Type> ReachedThroughReflection;

    /// <summary>Describes properties <paramref name="type"/> declares; <paramref name="declared"/> runs when one of them
    /// is first looked for. Called from generated module initializers. A type may be described more than once: the
    /// descriptions join, and the first of a name wins.</summary>
    public static void Register(Type type, Func<PropertyAccessor[]> declared)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declared);

        lock (RegistrationLock)
        {
            Described[type] = Described.TryGetValue(type, out var descriptions) ? [.. descriptions, declared] : [declared];
            stale = true;
        }
    }

    /// <summary>The property or field named <paramref name="name"/> on <paramref name="type"/>: the most derived
    /// property first, then one the type has through an interface, then a public field; null if there is none.</summary>
    public static PropertyAccessor Find(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);

        Refresh();
        return found.GetOrAdd((type, name), static key => Resolve(key.Type, key.Name));
    }

    /// <summary>The value of the property or field named <paramref name="name"/> on <paramref name="target"/>; null
    /// when the target or the name is null, or the target has no such value to read.</summary>
    public static object GetValue(object target, string name)
    {
        TryGetValue(target, name, out var value);
        return value;
    }

    /// <summary>Reads the property or field named <paramref name="name"/> on <paramref name="target"/>; false when the
    /// target or the name is null, or the target has no such value to read.</summary>
    public static bool TryGetValue(object target, string name, out object value)
    {
        if (target != null && name != null && Find(target.GetType(), name) is { CanRead: true } accessor)
        {
            value = accessor.GetValue(target);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>The property named <paramref name="name"/> that <paramref name="type"/> or one of its base classes
    /// declares - not one only an interface brings, such as a default implementation; null if there is none.</summary>
    public static PropertyAccessor FindDeclared(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);

        Refresh();
        for (var declaring = type; declaring != null; declaring = declaring.BaseType)
        {
            if (DeclaredBy(declaring) is { } described)
            {
                foreach (var accessor in described)
                {
                    if (accessor.Name == name)
                    {
                        return accessor;
                    }
                }
            }

            foreach (var property in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.Name == name && property.GetIndexParameters().Length == 0)
                {
                    Report(declaring);
                    return FromProperty(property);
                }
            }
        }

        return null;
    }

    private static PropertyAccessor Resolve(Type type, string name)
    {
        if (FindDeclared(type, name) is { } declaredProperty)
        {
            return declaredProperty;
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.GetProperty(name) is { } property && property.GetIndexParameters().Length == 0)
            {
                Report(contract);
                return FromProperty(property);
            }
        }

        if (type.GetField(name, BindingFlags.Public | BindingFlags.Instance) is { } field)
        {
            Report(field.DeclaringType ?? type);
            return new PropertyAccessor(field.Name, field.FieldType, field.GetValue, field.IsInitOnly ? null : field.SetValue);
        }

        return null;
    }

    private static void Refresh()
    {
        if (!stale)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (stale)
            {
                declared = new ConcurrentDictionary<Type, PropertyAccessor[]>();
                found = new ConcurrentDictionary<(Type Type, string Name), PropertyAccessor>();
                stale = false;
            }
        }
    }

    private static PropertyAccessor[] DeclaredBy(Type type)
    {
        var known = declared;
        if (known.TryGetValue(type, out var accessors))
        {
            return accessors;
        }

        Func<PropertyAccessor[]>[] descriptions;
        lock (RegistrationLock)
        {
            if (!Described.TryGetValue(type, out descriptions))
            {
                return null;
            }
        }

        HashSet<string> names = [];
        List<PropertyAccessor> joined = [];
        foreach (var description in descriptions)
        {
            foreach (var accessor in description())
            {
                if (names.Add(accessor.Name))
                {
                    joined.Add(accessor);
                }
            }
        }

        return known.GetOrAdd(type, [.. joined]);
    }

    private static PropertyAccessor FromProperty(PropertyInfo property)
    {
        return new PropertyAccessor(property.Name, property.PropertyType,
            property.GetMethod is { IsPublic: true } ? property.GetValue : null,
            property.SetMethod is { IsPublic: true } ? property.SetValue : null);
    }

    private static void Report(Type type)
    {
        if (!Reflected.TryAdd(type, 0))
        {
            return;
        }

        try
        {
            ReachedThroughReflection?.Invoke(type);
        }
        catch (Exception)
        {
        }
    }
}
