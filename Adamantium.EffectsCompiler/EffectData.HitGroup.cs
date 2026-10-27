using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>A hit group of a ray-tracing pass: its closest-hit, any-hit and intersection shaders, each at most once.
    /// </summary>
    [MessagePackObject]
    public sealed class HitGroup
    {
        /// <summary>The name of the hit group.</summary>
        [Key(0)]
        public string Name;

        /// <summary>The shaders of the group, each at its stage.</summary>
        [Key(1)]
        public List<ShaderLink> Shaders = [];

        /// <summary>A copy whose links can be changed without changing these.</summary>
        public HitGroup Clone() => new() { Name = Name, Shaders = [.. Shaders.Select(shader => shader.Clone())] };
    }
}
