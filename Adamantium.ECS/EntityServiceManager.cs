using Adamantium.Core;
using Adamantium.Core.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;

namespace Adamantium.ECS
{
    public sealed class EntityServiceManager
    {
        private readonly object syncObject = new object();

        private readonly Dictionary<UInt128, EntityService> activeServices;

        private readonly List<EntityService> servicesToAdd;
        private readonly List<EntityService> servicesToRemove;
        private readonly List<EntityService> pendingServices;

        private readonly AdamantiumCollection<EntityService> services;
        
        private IAdamantiumApplication appService;

        public EntityWorld EntityWorld { get; }
        
        /// <summary>
        /// A service registry that provides methods to register and unregister services.
        /// </summary>
        public IDependencyResolver Container { get; }

        internal EntityServiceManager(EntityWorld world)
        {
            EntityWorld = world;
            Container = EntityWorld.DependencyResolver;
            services = new AdamantiumCollection<EntityService>();
            activeServices = new Dictionary<UInt128, EntityService>();
            servicesToAdd = new List<EntityService>();
            servicesToRemove = new List<EntityService>();
            pendingServices = new List<EntityService>();
        }
        
        public IReadOnlyCollection<EntityService> Services => services.AsReadOnly();

        // The per-frame phases iterate this snapshot without a lock: a lock held through Draw made the loop wait out the
        // whole GPU frame. Republished in SyncServices, ordered by Priority (stable, so equal priorities keep their order).
        private volatile EntityService[] _snapshot = [];

        private void RepublishSnapshot() => _snapshot = services.OrderBy(s => s.Priority).ToArray();

        internal void OnServicePriorityChanged() => RepublishSnapshot();

        public Action FrameEnded;
        
        internal void InitializeResources()
        {
            appService = EntityWorld.Satellites.Get<IAdamantiumApplication>();
            appService.Started += OnServiceStarted;
            appService.ShuttingDown += OnServiceShuttingDown;
        }
        
        private void OnServiceShuttingDown(object sender, EventArgs e)
        {
            UnloadContent();
        }

        private void OnServiceAdded(EntityService service)
        {
            ServiceAdded?.Invoke(this, new EntityServiceEventArgs(service));
        }

        private void OnServiceRemoved(EntityService service)
        {
            service?.UnloadContent();
            ServiceRemoved?.Invoke(this, new EntityServiceEventArgs(service));
        }

        public T GetService<T>() where T : EntityService
        {
            foreach (var service in Services)
            {
                if (service is T variable)
                {
                    return variable;
                }
            }
            return null;
        }

        public T[] GetServices<T>() where T : EntityService
        {
            var list = new List<T>();
            foreach (var service in Services)
            {
                if (service is T variable)
                {
                    list.Add(variable);
                }
            }
            return list.ToArray();
        }

        public void Initialize()
        {
            lock (syncObject)
            {
                foreach (var service in pendingServices)
                {
                    service.Initialize();
                    service.LoadContent();
                }
                pendingServices.Clear();
            }
        }

        public void LoadContent()
        {
            lock (syncObject)
            {
                foreach (var service in Services)
                {
                    service.LoadContent();
                }
            }
        }

        public void UnloadContent()
        {
            lock (syncObject)
            {
                foreach (var service in Services)
                {
                    service.UnloadContent();
                }
            }
        }

        public void Update(AppTime appTime)
        {
            foreach (var handler in _snapshot)
            {
                handler.Update(appTime);
            }
        }

        /// <summary>Draws every rendering service that agrees to draw this frame. True when at least one did.</summary>
        public bool Draw(AppTime appTime)
        {
            var drew = false;
            foreach (var service in _snapshot)
            {
                if (!service.IsRenderingService) continue;

                if (!service.BeginDraw()) continue;

                drew = true;
                OnDrawStarted?.Invoke(service, appTime);
                service.Draw(appTime);
                service.EndDraw();
                OnDrawFinished?.Invoke(service, appTime);
                service.Submit();
            }

            return drew;
        }

        public void Present()
        {
            foreach (var service in _snapshot)
            {
                if (service.CanDisplayContent)
                {
                    service.Present();
                }
            }
            OnFrameEnded();
        }
        
        public void OnFrameEnded()
        {
            // The snapshot, not the locked collection: the lock here deadlocked against a service's init in SyncServices.
            foreach (var service in _snapshot)
            {
                service.FrameEnded();
            }
            FrameEnded?.Invoke();
            SyncServices();
        }

        // The lock guards only the collection edits; service callbacks run outside it, or the render thread deadlocks. A
        // service leaves the snapshot before its unload and enters it after its init, so it is never drawn half-made.
        internal void SyncServices()
        {
            EntityService[] toRemove, toAdd;
            lock (syncObject)
            {
                if (servicesToRemove.Count == 0 && servicesToAdd.Count == 0) return;
                toRemove = [.. servicesToRemove];
                servicesToRemove.Clear();
                toAdd = [.. servicesToAdd];
                servicesToAdd.Clear();
            }

            foreach (var service in toRemove)
            {
                if (service == null) continue;
                bool removed;
                lock (syncObject)
                {
                    removed = activeServices.Remove(service.Uid);
                    if (removed)
                    {
                        services.Remove(service);
                        RepublishSnapshot();
                    }
                }
                if (!removed) continue;
                service.UnloadContent();
                OnServiceRemoved(service);
            }

            foreach (var service in toAdd)
            {
                lock (syncObject)
                {
                    if (activeServices.ContainsKey(service.Uid)) continue;
                }
                if (appService.IsRunning)
                {
                    service.Initialize();
                    service.LoadContent();
                }
                lock (syncObject)
                {
                    activeServices[service.Uid] = service;
                    services.Add(service);
                    if (!appService.IsRunning) pendingServices.Add(service);
                    RepublishSnapshot();
                }
                OnServiceAdded(service);
            }
        }

        public void AddService(EntityService service)
        {
            lock (syncObject)
            {
                servicesToAdd.Add(service);
            }
        }

        public void AddServices(IEnumerable<EntityService> services)
        {
            foreach (var service in services)
            {
                AddService(service);
            }
        }

        public void RemoveService(UInt128 uid)
        {
            if (activeServices.TryGetValue(uid, out var service))
            {
                RemoveService(service);
            }
        }

        public void RemoveService(EntityService service)
        {
            lock (syncObject)
            {
                servicesToRemove.Add(service);
            }
        }

        public void RemoveAllServices()
        {
            foreach (var entityService in services)
            {
                RemoveService(entityService);
            }
        }

        public void RemoveServices(IEnumerable<EntityService> services)
        {
            foreach (var service in services)
            {
                RemoveService(service);
            }
        }

        private void OnServiceStarted(object sender, EventArgs e)
        {
            Initialize();
            LoadContent();
            foreach (var service in pendingServices)
            {
                service.Initialize();
                service.LoadContent();
            }
            pendingServices.Clear();
        }

        public void Reset()
        {
            lock (syncObject)
            {
                services.Clear();
                activeServices.Clear();
                servicesToAdd.Clear();
                servicesToRemove.Clear();
            }
        }
        
        public event EventHandler<EntityServiceEventArgs> ServiceAdded;
        public event EventHandler<EntityServiceEventArgs> ServiceRemoved;

        public event Action<IEntityService, AppTime> OnDrawStarted;

        public event Action<IEntityService, AppTime> OnDrawFinished; 
    }
}
