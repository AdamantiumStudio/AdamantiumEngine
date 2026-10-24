using Adamantium.Graphics.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>The descriptor sets' linker: a bound sampler state is resolved to its sampler once, when it is bound; a
/// pass writes the descriptors as it is applied.</summary>
internal sealed class SetsEffectResourceLinker(DescriptorSetManager sets) : EffectResourceLinker
{
    protected override void Bound(ResourceInfo<SamplerState> sampler) => sampler.Sampler = sets.SamplerOf(sampler.Resource);

    protected override void Bound(ResourceInfo<Texture> texture)
    {
    }

    protected override void Bound(ResourceInfo<Buffer> buffer)
    {
    }
}
