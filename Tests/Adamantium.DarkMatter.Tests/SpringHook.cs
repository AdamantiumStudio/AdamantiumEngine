namespace Adamantium.DarkMatter.Tests;

/// <summary>A damped spring pulling a body's center to an anchor, felt at every step.</summary>
internal sealed class SpringHook : IStepHook
{
    private readonly BodyId _body;
    private readonly WorldPosition _anchor;
    private readonly float _stiffness;
    private readonly float _damping;

    public SpringHook(BodyId body, WorldPosition anchor, float stiffness, float damping)
    {
        _body = body;
        _anchor = anchor;
        _stiffness = stiffness;
        _damping = damping;
    }

    public int Steps { get; private set; }

    public void OnStep(StepActions step)
    {
        Steps++;
        var stretch = step.CenterOfMassOf(_body).OffsetFrom(_anchor);
        step.ApplyForce(_body, -_stiffness * stretch - _damping * step.LinearVelocityOf(_body));
    }
}
