using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core.EffectsFramework;

namespace Adamantium.Graphics.Effects;

/// <summary>A resource of a pass and what of an effect holds it: the parameter, and for a uniform buffer the constant
/// buffer.</summary>
internal readonly record struct BoundPassResource(EffectData.PassResource Resource, EffectParameter Parameter,
    EffectConstantBuffer ConstantBuffer);
