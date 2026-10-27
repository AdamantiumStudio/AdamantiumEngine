using Adamantium.Core;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>A technique of an effect: its passes, in the order it declares them.</summary>
public sealed class EffectTechnique : NamedObject
{
    internal EffectTechnique(string name) : base(name)
    {
    }

    /// <summary>The passes of this technique.</summary>
    public EffectCollection<IEffectPass> Passes { get; } = new();
}
