using Adamantium.Core.DependencyInjection;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Physics;
using Adamantium.Engine.Scenes;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Events;

namespace Adamantium.UniverseHost;

/// <summary>The universe a game plays in outside the editor, with the services the editor gives a game: input, physics,
/// transforms and the game's code - none of the editor's own tools - and each output drawn as the game shows it.</summary>
internal sealed class GameUniverse : Universe
{
    private UniverseOutput _output;
    private Camera _sceneCamera;

    public GameUniverse(IDependencyContainer container) : base(UniverseMode.Standalone, false, container)
    {
        Title = "Game";
        IsFixedTimeStep = true;
        EventAggregator.GetEvent<UniverseOutputCreatedEvent>().Subscribe(OnOutputCreated);
    }

    /// <summary>Shows the game through <paramref name="camera"/>, a camera of its scene, now or once the output comes.
    /// On the universe's thread.</summary>
    public void LookThrough(Camera camera)
    {
        _sceneCamera = camera;
        if (_output != null)
            _output.Camera = camera;
    }

    protected override void Initialize()
    {
        base.Initialize();
        Satellites.Add(new Observatory(this, EntityWorld));
        Satellites.Add<IWorldCode>(new WorldCode());
        EntityWorld.CreateService<InputService>(EntityWorld);
        EntityWorld.CreateService<PhysicsService>(EntityWorld);
        EntityWorld.CreateService<TransformService>(EntityWorld);
    }

    private void OnOutputCreated(UniverseOutput output)
    {
        _output = output;
        var rendering = CreateRenderService<RenderingService>(output);
        rendering.AttachProcessor(new ForwardPlusRenderingProcessor());
        if (_sceneCamera != null)
            output.Camera = _sceneCamera;
    }
}
