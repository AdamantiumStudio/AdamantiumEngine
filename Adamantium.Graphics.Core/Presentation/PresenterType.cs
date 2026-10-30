namespace Adamantium.Graphics.Core.Presentation;

public enum PresenterType
{
    Swapchain = 0,
    RenderTarget = 1,
    /// <summary>A real swapchain on a window-less VK_EXT_headless_surface - for offscreen / designer output.</summary>
    Headless = 2,
    /// <summary>Each frame is read back to host memory and handed to <see cref="PresentationParameters.FrameSink"/> - for a
    /// window the desktop composes from pixels with their alpha, where the surface offers no transparent swapchain.</summary>
    Readback = 3
}
