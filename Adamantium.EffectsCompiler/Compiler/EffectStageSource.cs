namespace Adamantium.EffectsCompiler;

/// <summary>A stage of a pass as the effect file names it: <c>VertexShader = Main;</c>.</summary>
internal sealed class EffectStageSource(EffectStage stage, string entryPoint, SourceLocation location)
{
    public EffectStage Stage { get; } = stage;

    public string EntryPoint { get; } = entryPoint;

    public SourceLocation Location { get; } = location;
}
