using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.Multiverse.Events;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Payloads;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Serilog;

namespace Adamantium.Multiverse
{
    /// <summary>
    /// Keeps the universe's outputs and their input. The surfaces, the windows and the message loop come from the host,
    /// through <see cref="IOutputFactory"/> and <see cref="IWindowingPlatform"/>.
    /// </summary>
    public class UniversePlatform : IUniversePlatform, IDisposable
    {
        private List<UniverseOutput> windowsToAdd;
        private List<UniverseOutput> windowsToRemove;
        private readonly IUniverseEventAggregator _eventAggregator;
        private readonly IOutputFactory outputFactory;
        private readonly IWindowingPlatform windowingPlatform;
        private Dictionary<OutputContext, UniverseOutput> contextToWindow;

        private bool graphicsDeviceChanged;

        internal static int WindowId = 1;

        private AdamantiumCollection<UniverseOutput> outputs;

        private Dictionary<UniverseOutput, UniverseOutputParametersPayload> _changedOutputs;

        private readonly GamepadHub gamepads;

        private object syncObject = new object();

        public string DefaultAppDirectory
        {
            get
            {
                return Path.GetDirectoryName(Universe.GetType().Assembly.Location);
            }
        }

        /// <summary>
        /// Main <see cref="UniverseOutput"/>
        /// </summary>
        public UniverseOutput MainWindow { get; protected set; }

        /// <summary>
        /// Read only collection of <see cref="UniverseOutput"/>s
        /// </summary>
        public IReadOnlyList<UniverseOutput> Outputs => outputs;

        public bool HasOutputs => outputs.Count > 0;

        protected IUniverse Universe { get; }
        
        internal IGraphicsDeviceService GraphicsDeviceService { get; private set; }
        
        public UniversePlatform(IUniverse universe)
        {
            Universe = universe;
            universe.Initialized += Initialized;

            _eventAggregator = universe.Satellites.Get<IUniverseEventAggregator>();
            outputFactory = universe.Container.Resolve<IOutputFactory>();
            windowingPlatform = universe.Container.Resolve<IWindowingPlatform>();
            gamepads = new GamepadHub(universe.Container.Resolve<IGamepadBackend>(), _eventAggregator);
            _eventAggregator.GetEvent<UniverseOutputChangesRequestedEvent>()
                .Subscribe(OnUniverseOutputChangesRequested);

            _changedOutputs = new Dictionary<UniverseOutput, UniverseOutputParametersPayload>();
            outputs = new AdamantiumCollection<UniverseOutput>();
            windowsToAdd = new List<UniverseOutput>();
            windowsToRemove = new List<UniverseOutput>();
            contextToWindow = new Dictionary<OutputContext, UniverseOutput>();
        }

        private void OnUniverseOutputChangesRequested(UniverseOutputParametersPayload obj)
        {
            lock (syncObject)
            {
                // A full update covers a resize; a resize asked for after one must not downgrade it.
                if (_changedOutputs.TryGetValue(obj.Output, out var queued) && queued.Reason == ChangeReason.FullUpdate)
                {
                    return;
                }

                _changedOutputs[obj.Output] = obj;
            }
        }

        private void Initialized(object sender, EventArgs e)
        {
            GraphicsDeviceService = Universe.Satellites.Get<IGraphicsDeviceService>();
            GraphicsDeviceService.DeviceChangeBegin += DeviceChangeBegin;
            GraphicsDeviceService.DeviceChangeEnd += DeviceChangeEnd;
        }

        // The old device is still alive here: what the outputs made on it goes now, not after it is gone.
        private void DeviceChangeBegin(object sender, EventArgs e)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                outputs[i].ReleaseDeviceResources();
            }
        }

        private void DeviceChangeEnd(object sender, EventArgs e)
        {
            graphicsDeviceChanged = true;
        }

        /// <summary>
        /// Moves drawing from the old control to the new one; the old control can then be removed.
        /// </summary>
        public void SwitchContext(OutputContext oldContext, OutputContext newContext)
        {
            if (contextToWindow.Remove(oldContext, out var wnd))
            {
                wnd.SwitchContext(newContext);
                contextToWindow[newContext] = wnd;
            }
        }

        private void AddWindowsInternal()
        {
            if (windowsToAdd.Count > 0)
            {
                for (int i = 0; i < windowsToAdd.Count; i++)
                {
                    var wnd = windowsToAdd[i];
                    
                    var device = GraphicsDeviceService.CreateRenderDevice();
                    wnd.SetGraphicsDevice(device);
                    wnd.Input = new InputWormhole(wnd, gamepads, Universe);
                    SubscribeToEvents(wnd);
                    
                    outputs.Add(wnd);

                    if (outputs.Count == 1)
                    {
                        MainWindow = wnd;
                    }
                    wnd.ClearState();
                    wnd.Show();
                    OnWindowCreated(wnd);
                }
                windowsToAdd.Clear();
            }
        }

        private void Wnd_Closed(object sender, EventArgs e)
        {
            windowsToRemove.Add((UniverseOutput)sender);
        }

        internal void RemoveWindowsInternal()
        {
            if (windowsToRemove.Count > 0)
            {
                for (int i = 0; i < windowsToRemove.Count; i++)
                {
                    var wnd = windowsToRemove[i];
                    outputs.Remove(wnd);
                    UnsubscribeFromEvents(wnd);

                    WindowId--;
                    OnOutputRemoved(wnd);
                }
                windowsToRemove.Clear();
            }
        }

        private void SubscribeToEvents(UniverseOutput wnd)
        {
            wnd.Closed += Wnd_Closed;
        }

        private void UnsubscribeFromEvents(UniverseOutput wnd)
        {
            wnd.Closed -= Wnd_Closed;
        }

        /// <summary>
        /// Applies the queued output and device changes at the start of the frame.
        /// </summary>
        public void MakePreparationsForNextFrame()
        {
            RemoveWindowsInternal();

            lock (syncObject)
            {
                AddWindowsInternal();
                if (graphicsDeviceChanged)
                {
                    // The main device was made anew, and with it the logical device every output's resources lived on.
                    for (int i = 0; i < outputs.Count; i++)
                    {
                        var wnd = outputs[i];
                        var resized = wnd.ApplyRequestedSize();
                        wnd.OnWindowParametersChanging(ChangeReason.FullUpdate);
                        wnd.SetGraphicsDevice(GraphicsDeviceService.CreateRenderDevice());
                        wnd.OnWindowParametersChanged(ChangeReason.FullUpdate);
                        if (resized)
                        {
                            OnWindowSizeChanged(wnd);
                        }
                    }
                }
                else
                {
                    foreach (var wndObj in _changedOutputs)
                    {
                        var reason = wndObj.Value.Reason;
                        var wnd = wndObj.Key;
                        var resized = wnd.ApplyRequestedSize();

                        if (reason == ChangeReason.FullUpdate && wnd.Description.PresenterType != PresenterType.RenderTarget)
                        {
                            wnd.OnWindowParametersChanging(ChangeReason.FullUpdate);
                            var device = GraphicsDeviceService.MainGraphicsDevice.UpdateDevice(wnd.GraphicsDevice.DeviceId);
                            wnd.SetGraphicsDevice(device);
                            wnd.OnWindowParametersChanged(ChangeReason.FullUpdate);
                        }
                        else
                        {
                            // A render target rebuilds every buffer on resize, so it needs nothing more for a full update.
                            wnd.OnWindowParametersChanging(reason);
                            Log.Logger.Debug("Update universe output presenter");
                            wnd.UpdatePresenter();
                            wnd.OnWindowParametersChanged(reason);
                        }

                        if (resized)
                        {
                            OnWindowSizeChanged(wnd);
                        }
                    }
                }

                _changedOutputs.Clear();
                graphicsDeviceChanged = false;
            }
        }

        public void RemoveOutput(OutputContext context)
        {
            if (contextToWindow.Remove(context, out var window))
            {
                windowsToRemove.Add(window);
            }
        }

        private void OnWindowSizeChanged(UniverseOutput wnd)
        {
            wnd?.OnWindowSizeChanged();
        }

        /// <summary>
        /// Polls the gamepads once, then brings every output's input up to date for this frame.
        /// </summary>
        public void UpdateInput(AppTime time)
        {
            gamepads.Update();
            for (int i = 0; i < outputs.Count; i++)
            {
                outputs[i].Input.Update(time);
            }
        }

        private void OnWindowCreated(UniverseOutput output)
        {
            _eventAggregator.GetEvent<UniverseOutputCreatedEvent>().Publish(output);
        }

        private void OnOutputRemoved(UniverseOutput output)
        {
            _eventAggregator.GetEvent<UniverseOutputRemovedEvent>().Publish(output);
        }

        public void Dispose()
        {
            lock (this)
            {
                if (GraphicsDeviceService != null)
                {
                    GraphicsDeviceService.DeviceChangeBegin -= DeviceChangeBegin;
                    GraphicsDeviceService.DeviceChangeEnd -= DeviceChangeEnd;
                }

                for (int i = 0; i < Outputs.Count; i++)
                {
                    Outputs[i].Dispose();
                }
                outputs.Clear();
                MainWindow = null;
                contextToWindow.Clear();
            }
        }

        public void Run(CancellationToken token)
        {
            windowingPlatform.Run(token);
        }

        public UniverseOutput CreateOutput(uint width = 1280, uint height = 720)
        {
            var wnd = windowingPlatform.CreateWindow(width, height, _eventAggregator);
            windowsToAdd.Add(wnd);
            return wnd;
        }

        /// <summary>
        /// Creates an output on the control in <paramref name="context"/>.
        /// </summary>
        public virtual UniverseOutput CreateOutput(OutputContext context)
        {
            if (context == null)
            {
                return null;
            }

            var wnd = outputFactory.Create(context, _eventAggregator);
            contextToWindow.Add(context, wnd);
            windowsToAdd.Add(wnd);
            return wnd;
        }

        /// <summary>
        /// Creates an output on <paramref name="context"/>, a control.
        /// </summary>
        public UniverseOutput CreateOutput(object context)
        {
            var outputContext = new OutputContext(context);
            return CreateOutput(outputContext);
        }

        /// <summary>
        /// Creates an output on <paramref name="context"/> with the given formats, or returns the one it already has.
        /// </summary>
        public UniverseOutput CreateOutput(
            object context,
            SurfaceFormat surfaceFormat, 
            DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
            MSAALevel msaaLevel = MSAALevel.None)
        {
            var outputContext = new OutputContext(context);
            if (!contextToWindow.ContainsKey(outputContext))
            {
                var wnd = outputFactory.Create(outputContext, _eventAggregator, surfaceFormat, depthFormat, msaaLevel);
                contextToWindow.Add(outputContext, wnd);
                windowsToAdd.Add(wnd);
                return wnd;
            }
            return contextToWindow[outputContext];
        }

        /// <summary>
        /// Adds an output; it joins at the start of the next frame.
        /// </summary>
        public void AddOutput(UniverseOutput window)
        {
            if (outputs.Contains(window)) return;

            if (!contextToWindow.ContainsKey(window.OutputContext) && !windowsToAdd.Contains(window))
            {
                contextToWindow.Add(window.OutputContext, window);
                windowsToAdd.Add(window);
            }
        }

        /// <summary>
        /// Removes the output of the control <paramref name="context"/>.
        /// </summary>
        public void RemoveOutput(object context)
        {
            var outputContext = new OutputContext(context);
            RemoveOutput(outputContext);
        }
    }
}
