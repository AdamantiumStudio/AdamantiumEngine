using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Adamantium.Core.DependencyInjection
{
    internal class DependencyInjectionDetails
    {
        public DependencyInjectionDetails(LifeTimeVariant lifeTimeVariant)
        {
            LifeTimeVariant = lifeTimeVariant;
            DependencyItems = new List<DependencyItem>();
        }
        
        public LifeTimeVariant LifeTimeVariant { get; set; }
        
        public List<DependencyItem> DependencyItems { get; set; }

        public bool AlreadyExists(Type implementation, object instance, string name = "")
        {
            var contains = DependencyItems
                .FirstOrDefault(x => x.ImplementationType == implementation && x.Instance == instance && x.RegistryName == name);

            return contains != null;
        }

        public void AddDependency(Type implementation, object instance, string name = "")
        {
            if (AlreadyExists(implementation, instance, name)) throw new ArgumentException($"{implementation.Name} already registered");

            var item = new DependencyItem(implementation, instance, name);
            //If there are no instance, we should find constructor, which we will use to create an instance
            if (instance == null)
            {
                var constructors = implementation.GetConstructors().ToList();

                if (constructors.Count > 1)
                {
                    var ctor = constructors.FirstOrDefault(x => x.GetCustomAttribute<DependencyConstructorAttribute>() != null);
                    if (ctor != null)
                    {
                        item.Ctor = ctor;
                    }
                    else
                    {
                        throw new ArgumentException($"you should mark corresponding constructor for {implementation.Name}");                        
                    }
                }
                else
                {
                    item.Ctor = constructors.FirstOrDefault();
                }
            }
            DependencyItems.Add(item);
        }

        public object GetDependency(AdamantiumDependencyContainer container, Type type, string name,
            HashSet<Type> resolving)
        {
            var service = type;
            DependencyItem item = DependencyItems.FirstOrDefault(x => x.RegistryName == name);
            
            if (item == null) throw new ArgumentException($"Cannot resolve {service.Name} because its not registered");

            if (LifeTimeVariant is LifeTimeVariant.Transient or LifeTimeVariant.Singleton)
            {
                var parameters = new List<object>();
                ConstructionResolver given = null;
                if (item.Ctor != null)
                {
                    foreach (var parameter in item.Ctor.GetParameters())
                    {
                        // The container resolves the dependency, handling an alias redirect, the auto-register
                        // fallback, or a strict-mode throw - see AdamantiumDependencyContainer.Resolve.
                        var resolved = container.Resolve(parameter.ParameterType, "", resolving);
                        if (ReferenceEquals(resolved, container) && parameter.ParameterType.IsInterface)
                        {
                            resolved = given ??= new ConstructionResolver(container, resolving);
                        }

                        parameters.Add(resolved);
                    }
                }

                object instance = null;
                if (LifeTimeVariant == LifeTimeVariant.Transient || (LifeTimeVariant == LifeTimeVariant.Singleton && item.Instance == null))
                {
                    try
                    {
                        instance = Activator.CreateInstance(item.ImplementationType, parameters.ToArray());
                    }
                    finally
                    {
                        given?.Close();
                    }

                    if (LifeTimeVariant == LifeTimeVariant.Singleton)
                    {
                        item.Instance = instance;
                    }
                }
                else if (LifeTimeVariant == LifeTimeVariant.Singleton && item.Instance != null)
                {
                    instance = item.Instance;
                }

                return instance;
            }
            else
            {
                throw new NotSupportedException();
            }
        }
        
    }
}