using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;

namespace Adamantium.Graphics.Core;

/// <summary>Presents through the system's compositor rather than a swapchain of its own: it draws into a render target,
/// as <see cref="RenderTargetGraphicsPresenter"/> does, and hands each frame drawn to
/// <see cref="PresentationParameters.FrameSink"/>, which gives it to the compositor - a layered window on Windows, for a
/// see-through window whose surface offers no swapchain that keeps its alpha. The frame is read back to host memory
/// and the read waits for it, so it suits a window drawn now and then - an overlay - not one drawn every frame.</summary>
public class CompositorPresenter : RenderTargetGraphicsPresenter
{
    private Image _frame;

    public CompositorPresenter(IGraphicsDevice graphicsDevice, PresentationParameters description,
        string name = "") : base(graphicsDevice, description, name)
    {
    }

    public override PresenterState Present()
    {
        if (!CanPresent || Description.FrameSink is not { } sink)
        {
            return PresenterState.Success;
        }

        CanPresent = false;

        if (_frame == null || _frame.Description.Width != Width || _frame.Description.Height != Height)
        {
            _frame?.Dispose();
            _frame = Image.New2D(Width, Height, SurfaceFormat);
        }

        ResolveTexture.ReadbackInto(_frame);
        var pixels = _frame.PixelBuffer[0];
        sink.Show(pixels.DataPointer, Width, Height, (uint)pixels.RowStride);
        return PresenterState.Success;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        _frame?.Dispose();
        _frame = null;
        base.Dispose(disposeManagedResources);
    }
}
