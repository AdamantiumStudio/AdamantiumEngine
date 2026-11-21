namespace Adamantium.DarkMatter;

/// <summary>Work a <see cref="PhysicsSimulation"/> does at every step, before it solves: forces that change from step to
/// step - a spring of the caller's own, a wheel's suspension. Called on the thread that steps, which may be the
/// simulation's own: it must not touch what the caller's thread uses, and should allocate nothing.</summary>
public interface IStepHook
{
    /// <summary>Acts on the bodies for the step about to be solved.</summary>
    void OnStep(StepActions step);
}
