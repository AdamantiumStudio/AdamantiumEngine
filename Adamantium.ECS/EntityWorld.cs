using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS.Templates;

namespace Adamantium.ECS
{
    public class EntityWorld
    {
        public EntityWorld(IDependencyResolver container, Satellites satellites)
        {
            DependencyResolver = container ?? throw new ArgumentNullException($"{nameof(container)} should not be null");
            Satellites = satellites ?? throw new ArgumentNullException($"{nameof(satellites)} should not be null");
            EntityManager = new EntityManager(this);
            ServiceManager = new EntityServiceManager(this);
            ServiceManager.FrameEnded += FrameEnded;
        }

        public ReadOnlyTrackingCollection<Entity> RootEntities => EntityManager.RootEntities;
        public EntityManager EntityManager { get; }
        public EntityServiceManager ServiceManager { get; }
        public IDependencyResolver DependencyResolver { get; }

        /// <summary>
        /// The one-of-a-kind objects of whoever owns this world - its universe, or the application.
        /// </summary>
        public Satellites Satellites { get; }

        public void Initialize()
        {
            EntityManager.InitializeResources();
            ServiceManager.InitializeResources();
        }

        private void FrameEnded()
        {
            EntityManager.SyncEntities();
        }
        
        public async Task<Entity> CreateEntityFromTemplate(
            IEntityTemplate template, 
            Entity owner = null, 
            string name = "", 
            bool createDisabled = false)
        {
            return await EntityManager.CreateEntityFromTemplate(template, owner, name, createDisabled);
        }

        public T GetService<T>() where T : EntityService
        {
            return ServiceManager.GetService<T>();
        }

        public T[] GetServices<T>() where T : EntityService
        {
            return ServiceManager.GetServices<T>();
        }

        public T CreateService<T>(params object[] args) where T : EntityService
        {
            var service = (T)Activator.CreateInstance(typeof(T), args);
            AddService(service);
            return service;
        }

        public void AddService(EntityService service)
        {
            ServiceManager.AddService(service);
        }

        public void RemoveService(EntityService service)
        {
            ServiceManager.RemoveService(service);
        }

        public void RemoveService(UInt128 processorId)
        {
            ServiceManager.RemoveService(processorId);
        }
        
        public void AddServices(params EntityService[] services)
        {
            ServiceManager.AddServices(services);
        }
        
        public void RemoveAllServices()
        {
            ServiceManager.RemoveAllServices();
        }

        public void RemoveServices(params EntityService[] services)
        {
            ServiceManager.RemoveServices(services);
        }

        public EntityGroup CreateGroup(string groupName)
        {
            return EntityManager.CreateGroup(groupName);
        }
        
        public void RemoveAllEntities()
        {
            EntityManager.RemoveAllEntities();
        }

        public void Reset()
        {
            EntityManager.Reset();
            ServiceManager.Reset();
            WorldReseted?.Invoke(this, EventArgs.Empty);
        }

        public void Merge(EntityWorld world)
        {
            // Every entity of it, an owned one as well: a camera following a model is not among the roots.
            foreach (var entity in world.EntityManager.Entities)
            {
                EntityManager.AddEntity(entity);
            }

            foreach (var group in world.EntityManager.GetAllGroups())
            {
                EntityManager.AddGroup(group);
            }
        }

        public void ForceUpdate()
        {
            EntityManager.SyncEntities();
            ServiceManager.SyncServices();
        }
        
        public event EventHandler<EventArgs> WorldReseted;
    }
}
