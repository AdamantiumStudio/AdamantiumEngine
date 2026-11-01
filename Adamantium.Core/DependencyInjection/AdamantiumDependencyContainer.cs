using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.Core.Events;

namespace Adamantium.Core.DependencyInjection
{
    public class AdamantiumDependencyContainer : IDependencyContainer
    {
        // Concurrent because resolution is no longer a one-thread affair: a view can be built off the loop thread while
        // the loop resolves something of its own, and the lenient path WRITES here (auto-registering a type it was asked
        // for). A plain dictionary torn by that reads as a missing registration - or as nothing at all.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, DependencyInjectionDetails> _registrations;

        // Service-type -> concrete implementation redirect, populated by AutoRegister so an interface resolves to the
        // single registration of its implementation (keeps a singleton shared across interface and concrete resolves).
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type> _aliases = new();

        /// <summary>
        /// When true, resolving an unregistered type throws instead of auto-registering it as Transient. Turn this on
        /// to surface a forgotten registration (e.g. a type meant to be a singleton) rather than silently getting a
        /// fresh transient. Default false (lenient: auto-register + warn through <see cref="Log"/>).
        /// </summary>
        public bool StrictResolution { get; set; }

        /// <summary>Sink for diagnostic messages (auto-registration fallbacks). Defaults to the engine's Serilog logger
        /// (Warning level) so fallbacks land in the real log; reassign to redirect.</summary>
        public Action<string> Log { get; set; } = Serilog.Log.Warning;
        
        public AdamantiumDependencyContainer()
        {
            _registrations = new System.Collections.Concurrent.ConcurrentDictionary<Type, DependencyInjectionDetails>();
            RegisterSingleton<IEventAggregator, EventAggregator>();
        }

        public bool IsRegistered<T>()
        {
            return IsRegistered(typeof(T));
        }

        public bool IsRegistered(Type type)
        {
            return _registrations.ContainsKey(type);
        }

        IContainerRegistry IContainerRegistry.Register<TService, TImplementation>()
        {
            ((IContainerRegistry)this).Register(typeof(TService), typeof(TImplementation));
            return this;
        }

        IContainerRegistry IContainerRegistry.Register(Type service, Type implementation)
        {
            Register(LifeTimeVariant.Transient, service, implementation);
            return this;
        }

        IContainerRegistry IContainerRegistry.RegisterSingleton<T>()
        {
            ((IContainerRegistry)this).RegisterSingleton(typeof(T), typeof(T));
            return this;
        }

        public IContainerRegistry RegisterSingleton<TService, TImplementation>()
        {
            ((IContainerRegistry)this).RegisterSingleton(typeof(TService), typeof(TImplementation));
            return this;
        }

        public IContainerRegistry RegisterSingleton<TService>(object implementation)
        {
            Register(LifeTimeVariant.Singleton, typeof(TService), implementation.GetType(), implementation);
            return this;
        }

        IContainerRegistry IContainerRegistry.RegisterSingleton(Type service, Type implementation)
        {
            Register(LifeTimeVariant.Singleton, service, implementation);
            return this;
        }

        IContainerRegistry IContainerRegistry.RegisterInstance<TService>(object instance, string name)
        {
            ((IContainerRegistry)this).RegisterInstance(typeof(TService), instance, name);
            return this;
        }

        IContainerRegistry IContainerRegistry.RegisterInstance(Type source, object instance, string name)
        {
            Register(LifeTimeVariant.Singleton, source, instance.GetType(), instance, name);
            return this;
        }

        public T Resolve<T>(string name = "")
        {
            return (T)Resolve(typeof(T), name);
        }

        public object Resolve(Type type, string name = "") => Resolve(type, name, null);

        /// <summary>Resolves <paramref name="type"/> as one link of a chain: <paramref name="resolving"/> holds the types
        /// the chain is resolving, to fail fast on a circular dependency; null starts a chain. A chain is one call
        /// stack's, so two resolving at once never read each other's; a constructor given the container resolves on the
        /// chain while it runs (<see cref="ConstructionResolver"/>).</summary>
        internal object Resolve(Type type, string name, HashSet<Type> resolving)
        {

            // Redirect an interface/base service to its concrete implementation (AutoRegister alias), unless the type
            // itself has an explicit registration.
            if (!_registrations.ContainsKey(type) && _aliases.TryGetValue(type, out var impl))
            {
                type = impl;
            }

            if (!_registrations.TryGetValue(type, out var info))
            {
                if (StrictResolution)
                {
                    throw new ArgumentException($"{type.Name} is not registered (StrictResolution is enabled).");
                }

                if (type.IsInterface || type.IsAbstract)
                {
                    throw new ArgumentException(
                        $"{type.Name} is not registered and cannot be auto-created because it is an interface or abstract type. Register an implementation for it.");
                }

                // Variant-1 fallback: auto-register the concrete dependency as Transient so a chain of any depth
                // resolves without manual setup. Logged so it stays visible (a forgotten singleton surfaces here).
                Register(LifeTimeVariant.Transient, type, type);
                Log?.Invoke($"[DI] '{type.FullName}' was resolved but never registered - auto-registered as Transient.");
                info = _registrations[type];
            }

            resolving ??= new HashSet<Type>();
            if (!resolving.Add(type))
            {
                throw new ArgumentException($"Circular dependency detected while resolving '{type.FullName}'.");
            }

            try
            {
                return info.GetDependency(this, type, name, resolving);
            }
            finally
            {
                resolving.Remove(type);
            }
        }

        public IContainerRegistry AutoRegister(IEnumerable<Assembly> assemblies, params Type[] conventionBaseTypes)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type is not { IsClass: true, IsAbstract: false }) continue;

                    var serviceAttribute = type.GetCustomAttribute<ServiceAttribute>(false);
                    var matchesConvention = conventionBaseTypes != null &&
                                            conventionBaseTypes.Any(b => b != null && b != type && b.IsAssignableFrom(type));

                    if (serviceAttribute == null && !matchesConvention) continue;

                    var lifeTime = serviceAttribute != null ? Map(serviceAttribute.Lifetime) : LifeTimeVariant.Transient;

                    if (!IsRegistered(type))
                    {
                        Register(lifeTime, type, type);
                    }

                    // Alias each user-defined interface to this implementation so it resolves by interface too. First
                    // implementation wins; an interface with an explicit registration or alias is left untouched.
                    foreach (var contract in type.GetInterfaces())
                    {
                        if (contract.Namespace != null && contract.Namespace.StartsWith("System", StringComparison.Ordinal)) continue;
                        if (IsRegistered(contract) || _aliases.ContainsKey(contract)) continue;
                        _aliases[contract] = type;
                    }
                }
            }

            return this;
        }

        private static LifeTimeVariant Map(ServiceLifetime lifetime) =>
            lifetime == ServiceLifetime.Singleton ? LifeTimeVariant.Singleton : LifeTimeVariant.Transient;

        private void Register(LifeTimeVariant lifeTime, Type service, Type implementation, object instance = null,
            string name = "")
        {
            if (_registrations.TryGetValue(service, out var details))
            {
                details.AddDependency(implementation, instance, name);
            }
            else
            {
                details = new DependencyInjectionDetails(lifeTime);
                details.AddDependency(implementation, instance, name);
                _registrations[service] = details;
            }
        }
    }
}