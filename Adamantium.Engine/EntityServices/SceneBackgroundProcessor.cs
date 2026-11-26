using Adamantium.Core;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// The very first thing in an output's frame: the sky of the scene a <see cref="ForwardPlusRenderingProcessor"/> draws,
/// behind everything else - an editor's ground grid too, which lies over it. The scene's processor attaches it beside
/// itself and draws through its own resources.
/// </summary>
public sealed class SceneBackgroundProcessor : RenderingProcessor
{
    private readonly ForwardPlusRenderingProcessor scene;

    internal SceneBackgroundProcessor(ForwardPlusRenderingProcessor scene)
    {
        this.scene = scene;
    }

    /// <summary>Before every other processor of the frame.</summary>
    public override int Order => int.MinValue;

    public override void Draw(AppTime appTime) => scene.DrawBackground();

    // The scene's processor hands the frame on; drawn behind it, nothing is copied out here.
    public override void EndDraw()
    {
    }
}
