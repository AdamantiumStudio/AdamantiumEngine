using System;
using Adamantium.Vulkan.Slang;

namespace Adamantium.EffectsCompiler;

/// <summary>A stage of a pass or a hit group as the effect file names it: <c>VertexShader = Main;</c>.</summary>
public sealed class EffectStageSource
{
    internal EffectStageSource(EffectStage stage, string entryPoint, SourceLocation location)
    {
        Stage = stage;
        EntryPoint = entryPoint;
        Location = location;
    }

    public EffectStage Stage { get; }

    public string EntryPoint { get; }

    public SourceLocation Location { get; }

    /// <summary>The stage Slang compiles the entry point for.</summary>
    public SlangcStage SlangStage => Stage switch
    {
        EffectStage.Vertex => SlangcStage.Vertex,
        EffectStage.Hull => SlangcStage.Hull,
        EffectStage.Domain => SlangcStage.Domain,
        EffectStage.Pixel => SlangcStage.Fragment,
        EffectStage.Task => SlangcStage.Amplification,
        EffectStage.Mesh => SlangcStage.Mesh,
        EffectStage.Compute => SlangcStage.Compute,
        EffectStage.RayGeneration => SlangcStage.RayGeneration,
        EffectStage.Miss => SlangcStage.Miss,
        EffectStage.Callable => SlangcStage.Callable,
        EffectStage.ClosestHit => SlangcStage.ClosestHit,
        EffectStage.AnyHit => SlangcStage.AnyHit,
        EffectStage.Intersection => SlangcStage.Intersection,
        _ => throw new ArgumentOutOfRangeException(nameof(Stage), Stage, null)
    };
}
