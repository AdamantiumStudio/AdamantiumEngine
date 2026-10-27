namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>A stage a pass of an effect file names an entry point for - every stage the language describes, whether the
/// effect runtime runs it yet or not.</summary>
internal enum EffectStage
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
    Callable
}
