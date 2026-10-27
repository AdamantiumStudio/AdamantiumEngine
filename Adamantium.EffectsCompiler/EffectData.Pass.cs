using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>A pass: its name and the shaders it runs - one per stage, but misses and callables any number of
    /// times - and, for ray tracing, its hit groups.</summary>
    [MessagePackObject]
    public sealed class Pass
    {
        /// <summary>The name of the pass; null for none.</summary>
        [Key(0)]
        public string Name;

        /// <summary>The shaders of the pass's stages, in pipeline order whatever order the file names them in.</summary>
        [Key(1)]
        public List<ShaderLink> Stages = [];

        /// <summary>The hit groups of a ray-tracing pass.</summary>
        [Key(2)]
        public List<HitGroup> HitGroups = [];

        /// <summary>The resources the pass's stages read or write, each once.</summary>
        [Key(3)]
        public List<PassResource> Resources = [];

        /// <summary>On the descriptor heap: the bytes of push data the pass's resources take.</summary>
        [Key(4)]
        public uint PushDataSize;

        /// <summary>On descriptor sets: the number of descriptors the pass pushes.</summary>
        [Key(5)]
        public uint DescriptorCount;

        /// <summary>A copy whose links can be changed without changing these.</summary>
        public Pass Clone() => new()
        {
            Name = Name,
            Stages = [.. Stages.Select(stage => stage.Clone())],
            HitGroups = [.. HitGroups.Select(group => group.Clone())],
            Resources = [.. Resources],
            PushDataSize = PushDataSize,
            DescriptorCount = DescriptorCount
        };

        public override string ToString() => $"Pass: [{Name}], Stages({Stages.Count}), HitGroups({HitGroups.Count})";
    }
}
