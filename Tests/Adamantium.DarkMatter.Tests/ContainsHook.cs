namespace Adamantium.DarkMatter.Tests;

/// <summary>Tells whether the simulation held a body at the last step.</summary>
internal sealed class ContainsHook : IStepHook
{
    private readonly BodyId _body;

    public ContainsHook(BodyId body) => _body = body;

    public bool LastContained { get; private set; }

    public void OnStep(StepActions step) => LastContained = step.Contains(_body);
}
