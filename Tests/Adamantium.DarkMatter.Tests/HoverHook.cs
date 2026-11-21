namespace Adamantium.DarkMatter.Tests;

/// <summary>A push holding a body up against gravity, step by step.</summary>
internal sealed class HoverHook : IStepHook
{
    private readonly BodyId _body;
    private readonly Vector3F _gravity;

    public HoverHook(BodyId body, Vector3F gravity)
    {
        _body = body;
        _gravity = gravity;
    }

    public int Steps { get; private set; }

    public void OnStep(StepActions step)
    {
        Steps++;
        step.ApplyForce(_body, -_gravity * 10);
    }
}
