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

        /// <summary>The shaders of the pass's stages, in the order the file names them.</summary>
        [Key(1)]
        public List<ShaderLink> Stages = [];

        /// <summary>The hit groups of a ray-tracing pass.</summary>
        [Key(2)]
        public List<HitGroup> HitGroups = [];

        /// <summary>A copy whose links can be changed without changing these.</summary>
        public Pass Clone() => new()
        {
            Name = Name,
            Stages = [.. Stages.Select(stage => stage.Clone())],
            HitGroups = [.. HitGroups.Select(group => group.Clone())]
        };

        public override string ToString() => $"Pass: [{Name}], Stages({Stages.Count}), HitGroups({HitGroups.Count})";
    }
}
