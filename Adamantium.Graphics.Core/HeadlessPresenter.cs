using Adamantium.Graphics.Core.Presentation;

namespace Adamantium.Graphics.Core;

/// <summary>A swapchain presenter on a window-less VK_EXT_headless_surface, for offscreen output such as the AUML
/// designer; chosen by <see cref="PresenterType.Headless"/>.</summary>
public class HeadlessPresenter : SwapChainGraphicsPresenter
{
    public HeadlessPresenter(IGraphicsDevice graphicsDevice, PresentationParameters description, string name = "")
        : base(graphicsDevice, description, name)
    {
    }

    /// <summary>One: its frames are read back, not shown, and a ring would leave the copy just drawn behind a newer slot.
    /// </summary>
    protected override int FrameCopies => 1;
}