using Adamantium.Core.Events;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.Multiverse;

/// <summary>
/// An output with no surface: the universe draws it into a texture of a fixed size, which <see cref="ReadFrame"/> reads
/// back once the frame is done. It takes no input and is always shown. Added to a universe with
/// <see cref="Universe.AddOutput"/>.
/// </summary>
public sealed class OffscreenOutput : UniverseOutput
{
    public OffscreenOutput(uint width, uint height, MSAALevel msaaLevel = MSAALevel.X4)
        : base(new EventAggregator())
    {
        Description = new UniverseOutputDescription(PresenterType.RenderTarget)
        {
            Width = width,
            Height = height,
            PixelFormat = Format.B8G8R8A8_UNORM,
            MsaaLevel = msaaLevel
        };
        OutputContext = new OutputContext(this);
        ClientBounds = new Rectangle<int>(0, 0, (int)width, (int)height);
        UpdateViewportAndScissor(width, height);
    }

    public override UniverseOutputDescription Description { get; protected set; }

    public override OutputCursor Cursor { get; set; }

    public override object NativeWindow => null;

    public override OutputState State => OutputState.Shown;

    public override bool IsKeyboardFocused => false;

    public override bool IsPointerOver => false;

    public override Vector2F PointerScreenPosition => Vector2F.Zero;

    /// <summary>
    /// The last frame drawn into the output, in its pixel format; null before it has a device. Waits for the frames of
    /// the output's own device alone - not the whole GPU - so call it after the frame was submitted, on the thread that
    /// draws it.
    /// </summary>
    public Image ReadFrame()
    {
        if (Presenter?.GetCurrentImage() is not { } frame)
        {
            return null;
        }

        GraphicsDevice.WaitForFramesInFlight(ulong.MaxValue);
        return frame.ToImage();
    }

    public override Vector2F PointToSurface(Vector2F absolute)
    {
        return absolute;
    }

    public override void HoldPointer(bool hold, Vector2F origin)
    {
    }

    protected override bool CanHandle(OutputContext context)
    {
        return false;
    }

    protected internal override void SwitchContext(OutputContext context)
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
}
