using System;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public partial class EffectData
{
    /// <summary>A resource a shader reads or writes: where it is bound, how many descriptors it takes, and what the
    /// shader does with it.</summary>
    [MessagePackObject]
    public sealed class ResourceParameter : Parameter, IEquatable<ResourceParameter>
    {
        /// <summary>The descriptor set it is bound in.</summary>
        [Key(3)]
        public uint Set;

        /// <summary>Its binding in the set.</summary>
        [Key(4)]
        public uint Binding;

        /// <summary>The number of descriptors: 1 for one resource, 0 for an array of no length.</summary>
        [Key(5)]
        public uint Count;

        /// <summary>What the shader does with it: read it, write it or both.</summary>
        [Key(6)]
        public EffectResourceAccess Access;

        public bool Equals(ResourceParameter other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return base.Equals(other) && Set == other.Set && Binding == other.Binding && Count == other.Count
                   && Access == other.Access;
        }

        public override bool Equals(object obj) => obj is ResourceParameter other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Set;
                hashCode = (hashCode * 397) ^ (int)Binding;
                hashCode = (hashCode * 397) ^ (int)Count;
                hashCode = (hashCode * 397) ^ (int)Access;
                return hashCode;
            }
        }

        public static bool operator ==(ResourceParameter left, ResourceParameter right) => Equals(left, right);

        public static bool operator !=(ResourceParameter left, ResourceParameter right) => !Equals(left, right);
    }
}
