using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Adamantium.ECS;

/// <summary>The code a world finds types in: the assemblies of the game and its mods given to it, in that order, then
/// those of the application itself - the default load context. An assembly of a collectible context counts only while
/// it is given, so code replaced or unloaded is not found again.</summary>
public sealed class WorldCode
{
    private readonly object _lock = new();
    private IReadOnlyList<Assembly> _assemblies = [];

    /// <summary>The assemblies given, in the order they are searched.</summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>Puts <paramref name="arriving"/> in place of <paramref name="leaving"/>; either may be empty.</summary>
    public void Replace(IReadOnlyList<Assembly> leaving, IReadOnlyList<Assembly> arriving)
    {
        ArgumentNullException.ThrowIfNull(leaving);
        ArgumentNullException.ThrowIfNull(arriving);

        lock (_lock)
        {
            var kept = new List<Assembly>(_assemblies.Count + arriving.Count);
            foreach (var assembly in _assemblies)
            {
                if (!Contains(leaving, assembly))
                    kept.Add(assembly);
            }

            foreach (var assembly in arriving)
            {
                if (!kept.Contains(assembly))
                    kept.Add(assembly);
            }

            _assemblies = kept;
        }
    }

    /// <summary>Whether <paramref name="assembly"/> is one of those given.</summary>
    public bool IsGiven(Assembly assembly) => Contains(_assemblies, assembly);

    /// <summary>The type named <paramref name="fullName"/> deriving from <typeparamref name="T"/> that can be made - not
    /// abstract, with a public constructor taking nothing; when no type has that name, the one of the given assemblies
    /// that says it had it (<see cref="MovedFromAttribute"/>) - unless more than one says so. Null when the code has
    /// none.</summary>
    public Type FindType<T>(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return null;

        foreach (var assembly in _assemblies)
        {
            var type = FindMadeType<T>(assembly, fullName);
            if (type != null)
                return type;
        }

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (assembly.IsCollectible)
                continue;

            var type = FindMadeType<T>(assembly, fullName);
            if (type != null)
                return type;
        }

        return FindMovedType<T>(fullName);
    }

    // Two types claiming one former name is a mistake in the code; taking either would put the values on a guess.
    private Type FindMovedType<T>(string fullName)
    {
        Type found = null;
        foreach (var type in GetGivenTypes<T>())
        {
            if (!HadName(type, fullName))
                continue;

            if (found != null)
                return null;

            found = type;
        }

        return found;
    }

    private static bool HadName(Type type, string fullName)
    {
        foreach (var moved in type.GetCustomAttributes<MovedFromAttribute>(false))
        {
            if (string.Equals(moved.FullName, fullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The types deriving from <typeparamref name="T"/> that can be made, of the given assemblies alone - what
    /// the game and its mods bring.</summary>
    public IReadOnlyList<Type> GetGivenTypes<T>()
    {
        var found = new List<Type>();
        foreach (var assembly in _assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (IsMadeType<T>(type))
                    found.Add(type);
            }
        }

        return found;
    }

    private static Type FindMadeType<T>(Assembly assembly, string fullName)
    {
        var type = assembly.GetType(fullName, false);
        return type != null && IsMadeType<T>(type) ? type : null;
    }

    private static bool IsMadeType<T>(Type type) =>
        typeof(T).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters &&
        type.GetConstructor(Type.EmptyTypes) != null;

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return Array.FindAll(assembly.GetTypes(), type => type.IsVisible);
        }
        catch (ReflectionTypeLoadException exception)
        {
            return Array.FindAll(exception.Types, type => type != null && type.IsVisible);
        }
    }

    private static bool Contains(IReadOnlyList<Assembly> assemblies, Assembly assembly)
    {
        foreach (var each in assemblies)
        {
            if (ReferenceEquals(each, assembly))
                return true;
        }

        return false;
    }
}
