using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.Core.Events;
using Adamantium.ECS.Events;
using Adamantium.ECS.Payloads;
using Adamantium.Graphics.Core;
using Adamantium.MVVM;
using Serilog;

namespace Adamantium.ECS
{
    [ViewModel]
    public abstract partial class EntityService : IEntityService
    {
        [Bindable] private bool enabled;
        [Bindable] private ExecutionType updateExecutionType;
        [Bindable] private int priority;
        private int previousPriority;

        private ExecutionType drawExecutionType;
        [Bindable] private bool isVisible;

        protected IEventAggregator EventAggregator { get; }

        public EntityWorld EntityWorld { get; private set; }
        
        public IGraphicsDeviceService GraphicsDeviceService { get; protected set; }
        
        public IGraphicsDevice GraphicsDevice { get; protected set; }
        
        public abstract bool IsUpdateService { get; }
        public abstract bool IsRenderingService { get; }
        public abstract EntityServiceType ServiceType { get; }
        
        protected AppTime AppTime { get; set; }
        protected IDependencyResolver DependencyResolver { get; }

        /// <summary>
        /// Gets unique identifier for the system
        /// </summary>
        public UInt128 Uid { get; }

        protected EntityService(EntityWorld world)
        {
            Uid = UidGenerator.Generate();
            EntityWorld = world;
            DependencyResolver = EntityWorld.DependencyResolver;
            IsVisible = true;
            Enabled = true;
            EventAggregator = DependencyResolver.Resolve<IEventAggregator>();
            PropertyChanged += OnPropertyChanged;
        }

        // TODO: check does this property should return all available entities
        public IReadOnlyList<Entity> Entities => EntityWorld.RootEntities;

        /// <summary>
        /// Gets the name of this component.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        partial void OnPriorityChanging(int value)
        {
            if (priority != value) previousPriority = priority;
        }

        public virtual void Submit()
        {
        }

        public ExecutionType DrawExecutionType
        {
            get => drawExecutionType;
            set => drawExecutionType = value;
        }

        public virtual bool CanDisplayContent => true;
        
        private readonly List<IEntityProcessor> processors = [];

        public IReadOnlyList<IEntityProcessor> Processors => processors;

        public virtual void Initialize()
        {
        }

        public void AttachProcessor(IEntityProcessor processor)
        {
            if (processor == null || processors.Contains(processor)) return;
            processor.Attach(this);
            // Insert keeping the collection ordered by Order (stable: equal Order keeps attach order),
            // so draw/update order is deterministic (content < adorner < debug overlays ...).
            var index = processors.Count;
            while (index > 0 && processors[index - 1].Order > processor.Order) index--;
            processors.Insert(index, processor);
        }

        public void DetachProcessor(IEntityProcessor processor)
        {
            if (processor == null || !processors.Remove(processor)) return;
            processor.Detach();
        }

        public virtual void Update(AppTime appTime)
        {
            foreach (var processor in processors)
            {
                if (!processor.IsEnabled) continue;
                try { processor.Update(appTime); }
                catch (Exception ex) { Log.Logger.Error(ex, "Processor Update failed: {Processor}", processor.GetType().Name); }
            }
        }

        public virtual bool BeginDraw()
        {
            return IsVisible;
        }

        public virtual void Draw(AppTime appTime)
        {
            DrawProcessors(appTime);
        }

        public virtual void EndDraw()
        {
            EndDrawProcessors();
        }

        // Runs processors' Update explicitly (e.g. a synchronous one-shot render that doesn't go through the
        // service's own Update loop). Guarded so one processor can't take down the rest.
        protected void UpdateProcessors(AppTime appTime)
        {
            foreach (var processor in processors)
            {
                if (!processor.IsEnabled) continue;
                try { processor.Update(appTime); }
                catch (Exception ex) { Log.Logger.Error(ex, "Processor Update failed: {Processor}", processor.GetType().Name); }
            }
        }

        // Out-of-render-pass pass: a rendering service calls this from its BeginDraw beforeRenderPass hook so
        // processors can dispatch compute (e.g. the GPU stroke expander) before the render pass begins.
        protected void PreRenderProcessors()
        {
            foreach (var processor in processors)
            {
                if (!processor.IsEnabled) continue;
                try { processor.PreRender(); }
                catch (Exception ex) { Log.Logger.Error(ex, "Processor PreRender failed: {Processor}", processor.GetType().Name); }
            }
        }

        // Guarded, ordered iteration shared with rendering services that own the frame and call these
        // between their own BeginDraw/EndDraw. A throwing processor is logged and skipped so it cannot
        // take down the rest of the frame.
        protected void DrawProcessors(AppTime appTime)
        {
            foreach (var processor in processors)
            {
                if (!processor.IsEnabled) continue;
                try { processor.Draw(appTime); }
                catch (Exception ex) { Log.Logger.Error(ex, "Processor Draw failed: {Processor}", processor.GetType().Name); }
            }
        }

        protected void EndDrawProcessors()
        {
            foreach (var processor in processors)
            {
                if (!processor.IsEnabled) continue;
                try { processor.EndDraw(); }
                catch (Exception ex) { Log.Logger.Error(ex, "Processor EndDraw failed: {Processor}", processor.GetType().Name); }
            }
        }

        public virtual void LoadContent()
        {
            foreach (var processor in processors)
            {
                processor.LoadContent();
            }
        }

        public virtual void UnloadContent()
        {
            foreach (var processor in processors)
            {
                processor.UnloadContent();
            }
        }

        public virtual void Present()
        {

        }

        public virtual void FrameEnded()
        {
            
        }

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IsVisible):
                    EventAggregator.GetEvent<ProcessorVisibilityChangedEvent>().Publish(new ProcessorStatePayload(this, IsVisible));
                    break;
                case nameof(Enabled):
                    EventAggregator.GetEvent<ProcessorEnabledChangedEvent>().Publish(new ProcessorStatePayload(this, Enabled));
                    break;
                case nameof(Priority):
                    EntityWorld.ServiceManager.OnServicePriorityChanged();
                    EventAggregator.GetEvent<ProcessorPriorityChangedEvent>().Publish(new ProcessorPriorityPayload(this, previousPriority, Priority));
                    break;
                case nameof(UpdateExecutionType):
                    {
                        var prevResult = UpdateExecutionType == ExecutionType.Sync ? ExecutionType.Async : ExecutionType.Sync;
                        EventAggregator.GetEvent<ProcessorExecutionTypeChangedEvent>().Publish(new ProcessorExecutionTypePayload(this, ProcessorType.Update, prevResult, UpdateExecutionType));
                        break;
                    }
                case nameof(DrawExecutionType):
                    {
                        var prevResult = DrawExecutionType == ExecutionType.Sync ? ExecutionType.Async : ExecutionType.Sync;
                        EventAggregator.GetEvent<ProcessorExecutionTypeChangedEvent>().Publish(new ProcessorExecutionTypePayload(this, ProcessorType.Draw, prevResult, DrawExecutionType));
                        break;
                    }
            }
        }
    }


}