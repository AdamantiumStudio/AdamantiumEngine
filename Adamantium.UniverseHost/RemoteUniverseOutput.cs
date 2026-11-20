using Adamantium.Core.Events;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Remote;
using Adamantium.Vulkan.Core;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.UniverseHost;

/// <summary>An output shown in another process: its frames go to the viewer through shared surfaces, their handles and
/// frame numbers told it; its size, pointer, keyboard and input come from the viewer.</summary>
internal sealed class RemoteUniverseOutput : UniverseOutput, ISharedSurfaceConsumer
{
    private readonly IUniverseViewer _viewer;
    private readonly HandleTransfer _handles;
    private readonly ISharedSurfaceProducer _producer;
    private SharedSurfaceDescriptor _descriptor;
    private ulong _announced;
    private Vector2F _pointer;
    private volatile bool _pointerOver;
    private volatile bool _keyboardFocused;

    public RemoteUniverseOutput(IEventAggregator universeEvents, IUniverseViewer viewer, HandleTransfer handles,
        uint width, uint height) : base(universeEvents)
    {
        _viewer = viewer;
        _handles = handles;
        _producer = new SharedSurfaceProducer(this, SurfaceFormat.B8G8R8A8.UNorm);
        Description = new UniverseOutputDescription(PresenterType.RenderTarget)
        {
            Width = width,
            Height = height,
            PixelFormat = Format.B8G8R8A8_UNORM,
            MsaaLevel = MSAALevel.X4
        };
        OutputContext = new OutputContext(this);
        ClientBounds = new Rectangle<int>(0, 0, (int)width, (int)height);
        UpdateViewportAndScissor(width, height);
    }

    public override UniverseOutputDescription Description { get; protected set; }

    public override OutputCursor Cursor { get; set; }

    public override object NativeWindow => null;

    public override OutputState State => OutputState.Shown;

    public override bool IsKeyboardFocused => _keyboardFocused;

    public override bool IsPointerOver => _pointerOver;

    public override Vector2F PointerScreenPosition => _pointer;

    /// <summary>Draws at the size the viewer shows the frames at.</summary>
    public void ResizeTo(uint width, uint height, float pixelsPerPoint)
    {
        PixelsPerPoint = pixelsPerPoint;
        RequestResize(width, height);
    }

    /// <summary>The viewer asks for another frame: it has shown the ones before.</summary>
    public void TakeFrameRequest()
    {
        _descriptor?.Demand.Raise();
        Invalidate();
    }

    public void MovePointer(Vector2F position, bool isOver)
    {
        _pointer = position;
        _pointerOver = isOver;
        Invalidate();
    }

    public void Focus(bool hasKeyboard)
    {
        _keyboardFocused = hasKeyboard;
        Invalidate();
    }

    public void TakeKeyboardInput(KeyboardInput input) => OnKeyInput(input);

    public void TakeMouseInput(MouseInput input) => OnMouseInput(input);

    public void TakeGestureInput(GestureInput input) => OnGestureInput(input);

    public void TakeText(string text) => OnTextInput(text);

    /// <summary>The last frame drawn; null before the output has a device. On the thread that draws it, after the frame
    /// was submitted.</summary>
    public Image ReadFrame()
    {
        if (Presenter?.GetCurrentImage() is not { } frame)
        {
            return null;
        }

        GraphicsDevice.WaitForFramesInFlight(ulong.MaxValue);
        return frame.ToImage();
    }

    public override void CopyOutput(IGraphicsDevice graphicsDevice)
    {
        if (Presenter is RenderTargetGraphicsPresenter { ResolveTexture: { } frame })
        {
            _producer.Produce(graphicsDevice, frame, Width, Height, inDevice: false);
        }
    }

    public override void DisplayContent()
    {
        base.DisplayContent();
        _producer.NoteSubmitted();
    }

    public override bool WaitUntilShown(TimeSpan timeout) => _producer.WaitUntilShown(timeout);

    public override Vector2F PointToSurface(Vector2F absolute) => absolute;

    public override void HoldPointer(bool hold, Vector2F origin)
    {
    }

    void ISharedSurfaceConsumer.Bind(SharedSurfaceDescriptor descriptor)
    {
        _descriptor = descriptor;
        _announced = descriptor.Submitted;
        var buffers = new RemoteSurfaceBuffer[descriptor.BufferCount];
        buffers[0] = Give(descriptor);
        for (var i = 1; i < buffers.Length; i++)
        {
            buffers[i] = Give(descriptor.Others[i - 1]);
        }

        _viewer.ShowSurface(new RemoteSurface
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Format = descriptor.Format,
            HandleType = descriptor.HandleType,
            Buffers = buffers
        });
    }

    void ISharedSurfaceConsumer.Unbind(SharedSurfaceDescriptor descriptor)
    {
        if (ReferenceEquals(_descriptor, descriptor))
        {
            _descriptor = null;
        }
    }

    void ISharedSurfaceConsumer.NoteFrameSubmitted()
    {
        if (_descriptor is { } descriptor && descriptor.Submitted > _announced)
        {
            _announced = descriptor.Submitted;
            _viewer.ShowFrame(_announced);
        }
    }

    void ISharedSurfaceConsumer.AskFrame() => Invalidate();

    protected override void Dispose(bool disposeManagedResources)
    {
        _producer.Release(retire: true);
        base.Dispose(disposeManagedResources);
    }

    protected override void ReleaseDeviceResources()
    {
        _producer.Release(retire: false);
        base.ReleaseDeviceResources();
    }

    protected override bool CanHandle(OutputContext context) => false;

    protected override void SwitchContext(OutputContext context)
    {
    }

    protected override void Initialize(OutputContext context)
    {
    }

    protected override void Initialize(
        OutputContext context,
        SurfaceFormat pixelFormat,
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24,
        MSAALevel msaaLevel = MSAALevel.X4)
    {
    }

    private RemoteSurfaceBuffer Give(SharedSurfaceDescriptor surface) => new(
        _handles.Give(surface.MemoryHandle),
        surface.AllocationSize,
        _handles.Give(surface.ProduceSemaphoreHandle),
        _handles.Give(surface.ConsumeSemaphoreHandle));
}
