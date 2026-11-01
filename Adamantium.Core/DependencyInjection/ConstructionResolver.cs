using System;
using System.Collections.Generic;
using System.Reflection;

namespace Adamantium.Core.DependencyInjection;

/// <summary>The container as a constructor the container calls is given it: while that constructor runs, what it
/// resolves goes on the chain that is resolving it, so a loop back to a type on that chain is found; once it has run, it
/// is the container itself. Registrations go to the container as they are.</summary>
internal sealed class ConstructionResolver : IDependencyContainer
{
    private readonly AdamantiumDependencyContainer _container;
    private readonly IContainerRegistry _registry;
    private HashSet<Type> _resolving;

    public ConstructionResolver(AdamantiumDependencyContainer container, HashSet<Type> resolving)
    {
        _container = container;
        _registry = container;
        _resolving = resolving;
    }

    public bool StrictResolution
    {
        get => _registry.StrictResolution;
        set => _registry.StrictResolution = value;
    }

    /// <summary>The constructor has run: what is resolved from here on starts a chain of its own.</summary>
    public void Close() => _resolving = null;

    public T Resolve<T>(string name = "") => (T)Resolve(typeof(T), name);

    public object Resolve(Type type, string name = "") => _container.Resolve(type, name, _resolving);

    public bool IsRegistered<T>() => _registry.IsRegistered<T>();

    public bool IsRegistered(Type type) => _registry.IsRegistered(type);

    public IContainerRegistry Register<TService, TImplementation>() => _registry.Register<TService, TImplementation>();

    public IContainerRegistry Register(Type from, Type to) => _registry.Register(from, to);

    public IContainerRegistry RegisterSingleton<T>() => _registry.RegisterSingleton<T>();

    public IContainerRegistry RegisterSingleton<TService, TImplementation>() =>
        _registry.RegisterSingleton<TService, TImplementation>();

    public IContainerRegistry RegisterSingleton<TService>(object implementation) =>
        _registry.RegisterSingleton<TService>(implementation);

    public IContainerRegistry RegisterSingleton(Type service, Type implementation) =>
        _registry.RegisterSingleton(service, implementation);

    public IContainerRegistry RegisterInstance<TService>(object instance, string name = "") =>
        _registry.RegisterInstance<TService>(instance, name);

    public IContainerRegistry RegisterInstance(Type service, object implementation, string name = "") =>
        _registry.RegisterInstance(service, implementation, name);

    public IContainerRegistry AutoRegister(IEnumerable<Assembly> assemblies, params Type[] conventionBaseTypes) =>
        _registry.AutoRegister(assemblies, conventionBaseTypes);
}
