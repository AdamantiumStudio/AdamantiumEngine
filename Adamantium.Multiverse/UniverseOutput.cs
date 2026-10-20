using Adamantium.Core;
using Adamantium.Core.Events;
using Adamantium.ECS.Components;
using Adamantium.Multiverse.Events;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Payloads;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.Vulkan.Core;
using Rectangle = Adamantium.Mathematics.Rectangle;

namespace Adamantium.Multiverse
{
    /// <summary>
    /// Abstract class representing encapsulated rendering surface (control) with <see cref="GraphicsPresenter"/>
    /// </summary>
    [ViewModel]
    public abstract partial class UniverseOutput : DisposableObject
    {
        private Camera[] cameras = [];
        private readonly object camerasLock = new object();
        private Camera camera;
        private readonly object requestedSizeLock = new object();
        private uint requestedWidth;
        private uint requestedHeight;
        private bool sizeRequested;

        public Guid Id { get; }

        public IGraphicsDevice GraphicsDevice { get; private set; }
        
        protected IEventAggregator EventAggregator { get; }

        /// <summary>
        /// Contains <see cref="UniverseOutput"/> description
        /// </summary>
        public abstract UniverseOutputDescription Description { get; protected set; }

        public OutputContext OutputContext { get; protected set; }

        public static OutputCursor DefaultCursor = OutputCursor.Arrow;
        
        /// <summary>
        /// Bounds of the <see cref="UniverseOutput"/> starting always from (0,0)
        /// </summary>
        public Rectangle ClientBounds { get; protected set; }

        /// <summary>
        /// Cursor type that will be displayed when mouse cursor will enter <see cref="UniverseOutput"/> 
        /// </summary>
        public abstract OutputCursor Cursor { get; set; }

        /// <summary>
        /// Underlying control for rendering
        /// </summary>
        public abstract object NativeWindow { get; }

        /// <summary>
        /// Whether the output is on screen, and if not, why.
        /// </summary>
        public abstract OutputState State { get; }

        /// <summary>
        /// On screen: rendered and presented.
        /// </summary>
        public bool IsVisible => State == OutputState.Shown;

        /// <summary>
        /// Receives the keyboard: keyboard focus in the active OS window.
        /// </summary>
        public abstract bool IsKeyboardFocused { get; }

        /// <summary>
        /// The pointer is over the surface, or the surface holds it during a drag.
        /// </summary>
        public abstract bool IsPointerOver { get; }

        protected abstract bool CanHandle(OutputContext context);

        protected internal abstract void SwitchContext(OutputContext context);
        
        private void GenerateWindowName()
        {
            Name = $"Window_{UniversePlatform.WindowId++}";
        }

        protected UniverseOutput(IEventAggregator eventAggregator)
        {
            EventAggregator = eventAggregator;
            Viewport = new Viewport();
            Viewport.MaxDepth = 1.0f;
            Scissor = new Rect2D();
            GenerateWindowName();
            Id = Guid.NewGuid();
        }
        
        protected abstract void Initialize(OutputContext context);

        protected abstract void Initialize(
            OutputContext context, 
            SurfaceFormat pixelFormat, 
            DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24, 
            MSAALevel msaaLevel = MSAALevel.X4);

        public Rect2D Scissor { get; protected set; }

        public Viewport Viewport { get; protected set; }
        
        public GraphicsPresenter Presenter { get; protected set; }

        /// <summary>
        /// Keyboard, pointer and - while this output is the active one - gamepad input of this output.
        /// Set when the platform takes the output in; until then its input goes nowhere.
        /// </summary>
        public InputWormhole Input { get; internal set; }

        public virtual void CopyOutput(IGraphicsDevice graphicsDevice)
        {
            if (Presenter == null)
            {
                return;
            }

            graphicsDevice.BlitImage(GraphicsDevice.CurrentCommandBuffer,
                GraphicsDevice.CurrentRenderTarget,
                Presenter.GetCurrentImage());
        }

        /// <summary>
        /// Waits, up to <paramref name="timeout"/>, until whatever shows this output has room for another frame, so a
        /// universe on its own loop draws no more often than it is seen. Called before the frame, so the wait is no part of
        /// its drawing. Gives whether it has: a frame drawn without room is dropped. An output that presents by itself always
        /// has room.
        /// </summary>
        public virtual bool WaitUntilShown(TimeSpan timeout)
        {
            return true;
        }

        public void DisplayContent()
        {
            if (IsUpToDate())
            {
                Presenter.Present();
            }
        }

        public void SetGraphicsDevice(IGraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
            // Nothing to draw into until the output has an area: a presenter made now would be made of zero-size images.
            Presenter = HasArea ? GraphicsPresenter.Create(graphicsDevice, Description.ToPresentationParameters()) : null;
            ClearState();
            OnDeviceSet();
        }

        protected virtual void OnDeviceSet()
        {

        }

        // While the old device is still alive: the presenter goes with it, and the output is not drawn until it has a new one.
        protected internal virtual void ReleaseDeviceResources()
        {
            Presenter = null;
        }

        internal Boolean ResizeRequested { get; set; }

        internal Boolean UpdateRequested { get; set; }


        public Boolean IsUpToDate()
        {
            return Presenter != null && !ResizeRequested && !UpdateRequested;
        }

        private bool HasArea => Width > 0 && Height > 0;

        internal void ClearState()
        {
            ResizeRequested = false;
            UpdateRequested = false;
        }

        public virtual void Show()
        {
        }

        public virtual void Close()
        {
        }

        public virtual async Task TakeScreenshotAsync(string path, ImageFileType fileType)
        {
            await Presenter?.TakeScreenshotAsync(path, fileType);
        }

        public void UpdatePresenter()
        {
            // Without an area the old presenter stays, and the output is not drawn until it has one again.
            if (!HasArea)
            {
                return;
            }

            if (Presenter == null)
            {
                Presenter = GraphicsPresenter.Create(GraphicsDevice, Description.ToPresentationParameters());
            }
            else
            {
                ResizePresenter();
            }

            ClearState();
        }

        internal void ResizePresenter()
        {
            Presenter.Resize(Description);
        }

        /// <summary>
        /// Occurs when window size has changed
        /// </summary>
        public event Action<UniverseOutputSizeChangedPayload> SizeChanged;

        /// <summary>
        /// Occurs after GraphicsPresenter finish updating (recreated or resized)
        /// </summary>
        public event Action<UniverseOutputParametersPayload> ParametersChanged;

        /// <summary>
        /// Occurs before GraphicsPresenter updated (recreated or resized)
        /// </summary>
        public event Action<UniverseOutputParametersPayload> ParametersChanging;

        /// <summary>
        /// Occurs when window is closed
        /// </summary>
        public event EventHandler<EventArgs> Closed;

        /// <summary>
        /// Occurs when window state changed
        /// </summary>
        public Action<OutputState> StateChanged;

        internal void OnClosed()
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        internal void OnWindowParametersChanging(ChangeReason reason)
        {
            ParametersChanging?.Invoke(new UniverseOutputParametersPayload(this, Description, reason));
        }

        internal void OnWindowParametersChanged(ChangeReason reason)
        {
            ParametersChanged?.Invoke(new UniverseOutputParametersPayload(this, Description, reason));
        }

        internal void OnWindowSizeChanged()
        {
            SizeChanged?.Invoke(new UniverseOutputSizeChangedPayload(this, new Size(Width, Height)));
        }

        protected void OnKeyInput(KeyboardInput args)
        {
            Input?.OnKeyboardInput(args);
        }

        protected void OnTextInput(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Input?.OnTextInput(text);
            }
        }

        protected void OnMouseInput(MouseInput args)
        {
            Input?.OnMouseInput(args);
        }

        protected void OnGestureInput(GestureInput args)
        {
            Input?.OnGestureInput(args);
        }

        /// <summary>Asks the universe for frames, for what changes the picture without reaching it as input - the pointer
        /// moving over the output, say. Matters when it draws only on demand.</summary>
        protected void RequestFrame()
        {
            Input?.RequestFrame();
        }

        protected void UpdateViewportAndScissor(uint width, uint height)
        {
            Viewport.Width = width;
            Viewport.Height = height;

            Scissor.Extent = new Extent2D();
            Scissor.Extent.Width = width;
            Scissor.Extent.Height = height;
            Scissor.Offset = new Offset2D();

            lock (camerasLock)
            {
                var viewpoints = cameras;
                for (int i = 0; i < viewpoints.Length; i++)
                {
                    FitCamera(viewpoints[i], width, height);
                }
            }
        }

        /// <summary>
        /// Asks for a new size from any thread; applied at the start of the next universe frame, so no frame sees it change.
        /// </summary>
        protected void RequestResize(uint width, uint height)
        {
            lock (requestedSizeLock)
            {
                requestedWidth = width;
                requestedHeight = height;
                sizeRequested = true;
            }

            ResizeRequested = true;
            PublishChange(ChangeReason.Resize);
            RequestFrame();
        }

        // Before the presenter exists there is nothing to update: it is created from the description as it is then.
        private void RequestChange(ChangeReason reason)
        {
            if (Presenter == null)
            {
                return;
            }

            if (reason == ChangeReason.FullUpdate)
            {
                UpdateRequested = true;
            }
            else
            {
                ResizeRequested = true;
            }

            PublishChange(reason);
        }

        private void PublishChange(ChangeReason reason)
        {
            EventAggregator.GetEvent<UniverseOutputChangesRequestedEvent>()
                .Publish(new UniverseOutputParametersPayload(this, Description, reason));
        }

        internal bool ApplyRequestedSize()
        {
            uint width;
            uint height;
            lock (requestedSizeLock)
            {
                if (!sizeRequested)
                {
                    return false;
                }

                width = requestedWidth;
                height = requestedHeight;
                sizeRequested = false;
            }

            Width = width;
            Height = height;
            ClientBounds = new Rectangle(0, 0, (int)width, (int)height);
            UpdateViewportAndScissor(width, height);
            return true;
        }

        /// <summary>
        /// Pixels per point of the host's surface: the frame is drawn in pixels, sizes meant to look the same at any
        /// scale are points. Its cameras carry it too.
        /// </summary>
        public float PixelsPerPoint { get; protected set; } = 1;

        /// <summary>
        /// Viewpoints this output can show. Each is a live camera in the scene, and one camera may serve several
        /// outputs of the same size.
        /// </summary>
        public IReadOnlyList<Camera> Cameras => cameras;

        /// <summary>
        /// The viewpoint the output shows now. Setting a camera the output does not have yet adds it to <see cref="Cameras"/>.
        /// </summary>
        public Camera Camera
        {
            get => camera;
            set
            {
                if (value != null)
                {
                    AddCamera(value);
                }
                camera = value;
            }
        }

        public void AddCamera(Camera viewpoint)
        {
            lock (camerasLock)
            {
                if (Array.IndexOf(cameras, viewpoint) >= 0)
                {
                    return;
                }

                FitCamera(viewpoint, Width, Height);
                cameras = [..cameras, viewpoint];
            }
        }

        public void RemoveCamera(Camera viewpoint)
        {
            lock (camerasLock)
            {
                var index = Array.IndexOf(cameras, viewpoint);
                if (index < 0)
                {
                    return;
                }

                cameras = [..cameras[..index], ..cameras[(index + 1)..]];

                if (camera == viewpoint)
                {
                    camera = cameras.Length > 0 ? cameras[0] : null;
                }
            }
        }

        private void FitCamera(Camera viewpoint, uint width, uint height)
        {
            viewpoint.Width = width;
            viewpoint.Height = height;
            viewpoint.PixelsPerPoint = PixelsPerPoint;
            viewpoint.Initialize();
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            GraphicsDevice?.Dispose();
            base.Dispose(disposeManagedResources);
        }


        public virtual UInt32 Width
        {
            get => Description.Width;
            set
            {
                if (Description.Width != value)
                {
                    Description.Width = value;
                    RaisePropertyChanged();
                }
            }
        }

        public virtual UInt32 Height
        {
            get => Description.Height;
            set
            {
                if (Description.Height != value)
                {
                    Description.Height = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Where the pointer is on the screen, in the coordinates <see cref="PointToSurface"/> converts from.
        /// </summary>
        public abstract Vector2F PointerScreenPosition { get; }

        /// <summary>
        /// Converts a point in screen coordinates into the coordinates of the surface this output draws on.
        /// </summary>
        public abstract Vector2F PointToSurface(Vector2F absolute);

        /// <summary>
        /// Pins and hides the pointer, so a drag carries on past the screen edge as motion. False puts the cursor back.
        /// </summary>
        public abstract void HoldPointer(bool hold, Vector2F origin);

        public virtual IntPtr Handle
        {
            get => Description.Handle;
            protected set
            {
                if (Description.Handle != value)
                {
                    Description.Handle = value;
                    RequestChange(ChangeReason.FullUpdate);
                    RaisePropertyChanged();
                }
            }
        }

        public virtual SurfaceFormat PixelFormat
        {
            get => Description.PixelFormat;
            set
            {
                if (Description.PixelFormat != value)
                {
                    Description.PixelFormat = value;
                    RequestChange(ChangeReason.Resize);
                    RaisePropertyChanged();
                }
            }
        }

        public virtual DepthFormat DepthFormat
        {
            get => Description.DepthFormat;
            set
            {
                if (Description.DepthFormat != value)
                {
                    Description.DepthFormat = value;
                    RequestChange(ChangeReason.Resize);
                    RaisePropertyChanged();
                }
            }
        }

        public virtual MSAALevel MSAALevel
        {
            get => Description.MsaaLevel;
            set
            {
                if (Description.MsaaLevel != value)
                {
                    Description.MsaaLevel = value;
                    RequestChange(ChangeReason.FullUpdate);
                    RaisePropertyChanged();
                }
            }
        }

        public virtual UInt32 BuffersCount
        {
            get => Description.BuffersCount;
            set
            {
                if (Description.BuffersCount != value)
                {
                    Description.BuffersCount = value;
                    if (Description.PresenterType == PresenterType.Swapchain)
                    {
                        RequestChange(ChangeReason.Resize);
                    }
                    RaisePropertyChanged();
                }
            }
        }

        public virtual PresentInterval PresentInterval
        {
            get => Description.PresentInterval;
            set => Description.PresentInterval = value;
        }
    }
}
