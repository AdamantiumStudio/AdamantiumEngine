using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>A resource a pass reads or writes, once for all its stages, and where each descriptor model puts it: in
    /// the push data on the descriptor heap, among the pushed descriptors on descriptor sets.</summary>
    [MessagePackObject]
    public sealed class PassResource
    {
        /// <summary>The name of the resource, which its parameter goes by.</summary>
        [Key(0)]
        public string Name;

        /// <summary>What the resource is: a uniform buffer, a sampled image, a sampler or a storage buffer.</summary>
        [Key(1)]
        public EffectParameterType Type;

        /// <summary>The descriptor set it is bound in.</summary>
        [Key(2)]
        public uint Set;

        /// <summary>Its binding in the set.</summary>
        [Key(3)]
        public uint Binding;

        /// <summary>The number of descriptors: 1 for one resource, 0 for an array of no length.</summary>
        [Key(4)]
        public uint Count;

        /// <summary>On the descriptor heap: where in the push data the address of a uniform buffer, or the heap index of
        /// each element, lies.</summary>
        [Key(5)]
        public uint PushOffset;

        /// <summary>On descriptor sets: the first of its descriptors among those the pass pushes; -1 for a sampled image
        /// array of no length, which is bound as a set of its own.</summary>
        [Key(6)]
        public int Descriptor;

        public override string ToString() => $"{Type} {Name}: set {Set}, binding {Binding}, count {Count}";
    }
}
