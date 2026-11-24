using Adamantium.Core;
using Adamantium.Core.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Serilog;

namespace Adamantium.ECS
{
    public sealed class EntityServiceManager
    {
        private readonly object syncObject = new object();

        private readonly Dictionary<UInt128, IEntityService> activeServices;

        private readonly List<IEntityService> servicesToAdd;
        private readonly List<IEntityService> servicesToRemove;
        private readonly List<IEntityService> pendingServices;

        private readonly AdamantiumCollection<IEntityService> services;

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
            services = new AdamantiumCollection<IEntityService>();
            activeServices = new Dictionary<UInt128, IEntityService>();
            servicesToAdd = new List<IEntityService>();
            servicesToRemove = new List<IEntityService>();
            pendingServices = new List<IEntityService>();
        }

        public IReadOnlyCollection<IEntityService> Services => services.AsReadOnly();

        // The per-frame phases iterate this snapshot without a lock: a lock held through Draw made the loop wait out the
        // whole GPU frame. Republished in SyncServices, ordered by Priority (stable, so equal priorities keep their order).
        private volatile IEntityService[] _snapshot = [];
        // The service being updated: read by a drawing at a debugger's stop, the world's thread held - maybe inside it.
        private volatile IEntityService _updating;

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

        private void OnServiceAdded(IEntityService service)
        {
            ServiceAdded?.Invoke(this, new EntityServiceEventArgs(service));
        }

        private void OnServiceRemoved(IEntityService service)
        {
            service?.UnloadContent();
            ServiceRemoved?.Invoke(this, new EntityServiceEventArgs(service));
        }

        /// <summary>The first service that is a <typeparamref name="T"/> - a service type or an interface - or null.
        /// </summary>
        public T GetService<T>() where T : class
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

        /// <summary>Every service that is a <typeparamref name="T"/> - a service type or an interface.</summary>
        public T[] GetServices<T>() where T : class
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

        /// <summary>Unloads every service, those added since the last frame and not yet joined too.</summary>
        public void UnloadContent()
        {
            lock (syncObject)
            {
                foreach (var service in Services)
                {
                    service.UnloadContent();
                }

                foreach (var service in servicesToAdd)
                {
                    service.UnloadContent();
                }

                servicesToAdd.Clear();
            }
        }

        /// <summary>Updates each active service. One that throws and <see cref="IEntityService.SleepsWhenUpdateFails"/> is
        /// put to sleep, so as not to throw again every frame, and the others go on.</summary>
        public void Update(AppTime appTime)
        {
            foreach (var handler in _snapshot)
            {
                if (!handler.IsActive)
                    continue;

                _updating = handler;
                try
                {
                    handler.Update(appTime);
                }
                catch (Exception exception) when (handler.SleepsWhenUpdateFails)
                {
                    Log.Logger.Error(exception, "The service {Service} failed to update and is put to sleep",
                        handler.GetType().FullName);
                    handler.IsActive = false;
                }
                finally
                {
                    _updating = null;
                }
            }
        }

        /// <summary>Brings what each active service of the engine's draws from up to date with the world where the game
        /// stands - at a debugger's stop, before it is drawn there, on another thread, the world's own held. Only while it
        /// is held inside a game service's update, where breakpoints are: anywhere else the engine may be halfway through
        /// changing the world, and nothing is touched. The game's own services are left as they are - their code would run
        /// where no breakpoint stops it. One that fails leaves the others to it.</summary>
        public void UpdateWhereItStands()
        {
            if (_updating is not IGameService)
                return;

            foreach (var handler in _snapshot)
            {
                if (!handler.IsActive || handler is IGameService)
                    continue;

                try
                {
                    handler.UpdateWhereItStands();
                }
                catch (Exception exception)
                {
                    Log.Logger.Error(exception, "The service {Service} failed to bring the world where it stands up to date",
                        handler.GetType().FullName);
                }
            }
        }

        /// <summary>Whether an active rendering service is among the services: one with an output of its own draws even
        /// in a universe without outputs.</summary>
        public bool HasActiveRenderingService
        {
            get
            {
                foreach (var service in _snapshot)
                {
                    if (service.IsRenderingService && service.IsActive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Draws every active rendering service that agrees to draw this frame. True when at least one did.
        /// </summary>
        public bool Draw(AppTime appTime)
        {
            var drew = false;
            foreach (var service in _snapshot)
            {
                if (!service.IsRenderingService || !service.IsActive) continue;

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
                if (service.IsActive && service.CanDisplayContent)
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

        /// <summary>Puts in the services added and takes out those removed since the last frame ended, as the end of a
        /// frame does. On the world's thread, between its frames.</summary>
        // The lock guards only the collection edits; service callbacks run outside it, or the render thread deadlocks. A
        // service leaves the snapshot before its unload and enters it after its init, so it is never drawn half-made.
        public void SyncServices()
        {
            IEntityService[] toRemove, toAdd;
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
                PutIn(service);
            }
        }

        /// <summary>Puts in only the rendering services added since the last frame ended: for a drawing of the world
        /// where it stands, outside its frames, its thread held - in its first frame, the output that came with it would
        /// otherwise have nothing to draw it until an end of the frame that does not come.</summary>
        public void TakeInRenderingServices()
        {
            List<IEntityService> toAdd = null;
            lock (syncObject)
            {
                for (var index = 0; index < servicesToAdd.Count; index++)
                {
                    if (servicesToAdd[index].IsRenderingService)
                        (toAdd ??= []).Add(servicesToAdd[index]);
                }

                if (toAdd == null) return;

                servicesToAdd.RemoveAll(service => service.IsRenderingService);
            }

            foreach (var service in toAdd)
            {
                PutIn(service);
            }
        }

        // Initialized and loaded before it enters the snapshot, so it is never drawn half-made.
        private void PutIn(IEntityService service)
        {
            lock (syncObject)
            {
                if (activeServices.ContainsKey(service.Uid)) return;
            }
            // A world not yet given its application - not initialized - keeps the service waiting, as one whose
            // application has not started does.
            var running = appService is { IsRunning: true };
            if (running)
            {
                service.Initialize();
                service.LoadContent();
            }
            lock (syncObject)
            {
                activeServices[service.Uid] = service;
                services.Add(service);
                if (!running)
                    pendingServices.Add(service);
                RepublishSnapshot();
            }
            OnServiceAdded(service);
        }

        public void AddService(IEntityService service)
        {
            lock (syncObject)
            {
                servicesToAdd.Add(service);
            }
        }

        public void AddServices(IEnumerable<IEntityService> services)
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

        public void RemoveService(IEntityService service)
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

        public void RemoveServices(IEnumerable<IEntityService> services)
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
