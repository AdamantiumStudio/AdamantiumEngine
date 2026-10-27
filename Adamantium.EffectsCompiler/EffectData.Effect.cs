using System.Collections.Generic;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>An effect: its name, its techniques, and what it was compiled from when it can be compiled again.
    /// </summary>
    [MessagePackObject]
    public sealed class Effect
    {
        /// <summary>The name of the effect - its file's name.</summary>
        [Key(0)]
        public string Name;

        /// <summary>The techniques in the order the file has them.</summary>
        [Key(1)]
        public List<Technique> Techniques;

        /// <summary>What the effect was compiled from, to compile it again; null when it was not compiled to be.
        /// </summary>
        [Key(2)]
        public CompilerArguments Arguments;
    }
}
