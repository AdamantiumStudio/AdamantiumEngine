using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;

namespace Adamantium.Graphics.Core;

/// <summary>Presents through the system's compositor rather than a swapchain of its own: it draws into a render target,
/// as <see cref="RenderTargetGraphicsPresenter"/> does, and copies each frame drawn to <see cref="Frame"/>, which the
/// window gives to the compositor - a layered window on Windows, for a see-through window whose surface offers no
/// swapchain that keeps its alpha. The copy waits for the frame, so it suits a window drawn now and then - an
/// overlay - not one drawn every frame.</summary>
public class CompositorPresenter : RenderTargetGraphicsPresenter
{
    private Image _frame;

    public CompositorPresenter(IGraphicsDevice graphicsDevice, PresentationParameters description,
        string name = "") : base(graphicsDevice, description, name)
    {
    }

    /// <summary>The last frame presented, in host memory: B8G8R8A8 as drawn, top row first. Null until one is; reused
    /// for the next, so it is read before the next <see cref="Present"/>.</summary>
    public Image Frame => _frame;

    public override PresenterState Present()
    {
        if (!CanPresent)
        {
            return PresenterState.Success;
        }

        CanPresent = false;

        if (_frame == null || _frame.Description.Width != Width || _frame.Description.Height != Height)
        {
            _frame?.Dispose();
            _frame = Image.New2D(Width, Height, SurfaceFormat);
        }

        ResolveTexture.CopyTo(_frame);
        return PresenterState.Success;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        _frame?.Dispose();
        _frame = null;
        base.Dispose(disposeManagedResources);
    }
}
