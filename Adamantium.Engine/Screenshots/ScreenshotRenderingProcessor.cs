using System.Collections.Generic;
using System.Threading;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Screenshots;

/// <summary>Draws one entity, given by its <see cref="ScreenshotRenderingService"/>, through the camera and in the light
/// given with it, over a transparent ground; nothing of the world it may sit beside, and none of that world's lights.
/// </summary>
public sealed class ScreenshotRenderingProcessor : ForwardPlusRenderingProcessor
{
    private readonly Entity[] _drawn = new Entity[1];
    private Entity _target;
    private ScreenshotLighting _lighting = ScreenshotLighting.Studio();
    private ScreenshotLighting _frameLighting;

    public ScreenshotRenderingProcessor()
    {
        ClearColor = Colors.Transparent;
    }

    /// <summary>The entity drawn; null for nothing, and then nothing of the last one is held.</summary>
    public Entity Target
    {
        get => _target;
        set
        {
            _target = value;
            _drawn[0] = value;
            if (value == null)
            {
                ActiveCamera = null;
            }
        }
    }

    /// <summary>The camera it is drawn through.</summary>
    public Camera Camera { get; set; }

    /// <summary>The light it is drawn in, taken once a frame; set from any thread.</summary>
    public ScreenshotLighting Lighting
    {
        get => Volatile.Read(ref _lighting);
        set => Volatile.Write(ref _lighting, value ?? ScreenshotLighting.Studio());
    }

    protected override Camera ViewCamera => Camera;

    protected override IReadOnlyList<Entity> DrawnEntities => _target == null ? [] : _drawn;

    protected override IReadOnlyList<Light> DirectionalLights => FrameLighting.DirectionalLights;

    protected override IReadOnlyList<Light> PointLights => FrameLighting.PointLights;

    protected override IReadOnlyList<Light> SpotLights => FrameLighting.SpotLights;

    private ScreenshotLighting FrameLighting => _frameLighting ?? Lighting;

    public override void PreRender()
    {
        _frameLighting = Lighting;
        Ambient = _frameLighting.Ambient;
        base.PreRender();
    }
}
