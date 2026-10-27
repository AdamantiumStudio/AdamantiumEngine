namespace Adamantium.EffectsCompiler;

/// <summary>A stage a pass of an effect file names an entry point for - every stage the language describes, whether the
/// effect runtime runs it yet or not. ClosestHit, AnyHit and Intersection are named inside a hit group.</summary>
public enum EffectStage
{
    Vertex,
    Hull,
    Domain,
    Pixel,
    Task,
    Mesh,
    Compute,
    RayGeneration,
    Miss,
    Callable,
    ClosestHit,
    AnyHit,
    Intersection
}
