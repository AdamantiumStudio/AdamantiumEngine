using Adamantium.Core;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// The first thing in an output's frame: what the tools of a <see cref="ToolsOverlayProcessor"/> draw under the scene,
/// as the ground grid, which everything the scene draws then covers. The overlay attaches it beside itself and draws
/// through its own resources.
/// </summary>
public sealed class ToolsUnderlayProcessor : RenderingProcessor
{
    private readonly ToolsOverlayProcessor overlay;

    internal ToolsUnderlayProcessor(ToolsOverlayProcessor overlay)
    {
        this.overlay = overlay;
    }

    /// <summary>Before every other processor of the frame.</summary>
    public override int Order => int.MinValue;

    public override void Draw(AppTime appTime) => overlay.DrawUnderlays();

    // The overlay hands the frame on; drawn under the scene, nothing is copied out here.
    public override void EndDraw()
    {
    }
}
