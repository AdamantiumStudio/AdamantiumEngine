using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;

namespace Adamantium.Graphics.Core;

/// <summary>Draws into a render target, as <see cref="RenderTargetGraphicsPresenter"/> does, and presents a frame by
/// reading it back to host memory and handing it to <see cref="PresentationParameters.FrameSink"/>: for a window whose
/// desktop composes it from its pixels and their alpha, where the surface offers no swapchain that keeps them. The read
/// waits for the frame, so it suits a window drawn now and then - an overlay - not one drawn every frame.</summary>
public class ReadbackGraphicsPresenter : RenderTargetGraphicsPresenter
{
    private Image _frame;

    public ReadbackGraphicsPresenter(IGraphicsDevice graphicsDevice, PresentationParameters description,
        string name = "") : base(graphicsDevice, description, name)
    {
        // There is no image to acquire: every frame drawn can be presented.
        CanPresent = true;
    }

    public override PresenterState Present()
    {
        if (Description.FrameSink is not { } sink)
        {
            return PresenterState.Success;
        }

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
