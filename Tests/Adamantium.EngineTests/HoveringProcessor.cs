using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Physics;

namespace Adamantium.EngineTests;

/// <summary>A main processor in the base one's place, as a paid one would be: it holds an entity's body up against
/// gravity at every step through a step hook, and counts what the service asked of it.</summary>
internal sealed class HoveringProcessor : PhysicsProcessor, IStepHook
{
    private readonly Entity _entity;
    private BodyId _body;

    public HoveringProcessor(Entity entity) => _entity = entity;

    public int Starts { get; private set; }
    public int Frames { get; private set; }
    public int Stops { get; private set; }

    public void OnStep(StepActions step)
    {
        if (step.Contains(_body))
            step.ApplyForce(_body, -step.Gravity * step.MassOf(_body));
    }

    protected override void OnStarted()
    {
        Starts++;
        _body = FindBody(_entity);
        Simulation.AddStepHook(this);
    }

    protected override void AfterSteps() => Frames++;

    protected override void OnStopped()
    {
        Stops++;
        Simulation.RemoveStepHook(this);
    }
}
