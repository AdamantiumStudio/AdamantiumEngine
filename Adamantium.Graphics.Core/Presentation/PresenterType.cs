namespace Adamantium.Graphics.Core.Presentation;

public enum PresenterType
{
    Swapchain = 0,
    RenderTarget = 1,
    /// <summary>A real swapchain on a window-less VK_EXT_headless_surface - for offscreen / designer output.</summary>
    Headless = 2,
    /// <summary>Presented through the system's compositor (<see cref="CompositorPresenter"/>): each frame drawn is copied
    /// to host memory, for a window the desktop composes from its pixels with their alpha, where the surface offers no
    /// transparent swapchain.</summary>
    Compositor = 3
}
