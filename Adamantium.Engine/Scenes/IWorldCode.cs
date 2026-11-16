using System;
using System.Collections.Generic;
using System.Reflection;

namespace Adamantium.Engine.Scenes;

/// <summary>The code a world finds the types of its scenes in - the game's and its mods' assemblies, then the
/// application's - and the game services and components that code brings. One a world, put among its satellites
/// (<see cref="ECS.EntityWorld.Satellites"/>) by whoever hosts the world: the editor's universe, a game's player.
/// </summary>
public interface IWorldCode
{
    /// <summary>The assemblies given, in the order they are searched.</summary>
    IReadOnlyList<Assembly> Assemblies { get; }

    /// <summary>Puts <paramref name="arriving"/> in place of <paramref name="leaving"/>; either may be empty.</summary>
    void Replace(IReadOnlyList<Assembly> leaving, IReadOnlyList<Assembly> arriving);

    /// <summary>Whether <paramref name="assembly"/> is one of those given.</summary>
    bool IsGiven(Assembly assembly);

    /// <summary>The type named <paramref name="fullName"/> deriving from <typeparamref name="T"/> that can be made from a
    /// public constructor taking nothing; when no type has that name, the one of the given assemblies that says it had it
    /// (<see cref="MovedFromAttribute"/>). Null when the code has none.</summary>
    Type FindType<T>(string fullName);

    /// <summary>The types deriving from <typeparamref name="T"/> that can be made from a public constructor taking nothing,
    /// of the given assemblies alone - what the game and its mods bring.</summary>
    IReadOnlyList<Type> GetGivenTypes<T>();

    /// <summary>The types deriving from <typeparamref name="T"/> made from a public constructor taking just
    /// <paramref name="constructorParameters"/>, of the given assemblies alone.</summary>
    IReadOnlyList<Type> GetGivenTypes<T>(Type[] constructorParameters);
}
