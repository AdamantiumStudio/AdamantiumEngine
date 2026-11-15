using Adamantium.ECS;
using Adamantium.Mathematics;

namespace Adamantium.EngineTests;

/// <summary>A game service of the tests' code: moves every <see cref="Pushed"/> of the scene played along X by its speed,
/// and says when it was started and stopped.</summary>
public sealed class PushingService : GameService
{
    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public override void Start() => Started = true;

    public override void Update()
    {
        foreach (var pushed in Each<Pushed>())
        {
            pushed.Owner.Transform.Position += new Vector3<double>(pushed.Speed * FrameTime, 0, 0);
        }
    }

    public override void Stop() => Stopped = true;
}
