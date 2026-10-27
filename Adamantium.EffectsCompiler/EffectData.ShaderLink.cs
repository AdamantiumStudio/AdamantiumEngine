using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>A stage of a pass or a hit group and the shader it runs.</summary>
    [MessagePackObject]
    public sealed class ShaderLink
    {
        /// <summary>The stage the shader runs at.</summary>
        [Key(0)]
        public EffectStage Stage;

        /// <summary>Where the shader is in <see cref="EffectData.Shaders"/>.</summary>
        [Key(1)]
        public int Index;

        /// <summary>A copy whose index can be changed without changing this one's.</summary>
        public ShaderLink Clone() => new() { Stage = Stage, Index = Index };

        public override string ToString() => $"{Stage}: shader {Index}";
    }
}
