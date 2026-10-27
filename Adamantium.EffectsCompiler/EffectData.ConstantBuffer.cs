using System;
using System.Collections.Generic;
using Adamantium.Core;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public partial class EffectData
{
    /// <summary>A uniform buffer: where it is bound, its size and the values in it.</summary>
    [MessagePackObject]
    public sealed class ConstantBuffer : IEquatable<ConstantBuffer>
    {
        /// <summary>The name of the buffer.</summary>
        [Key(0)]
        public string Name;

        /// <summary>The size of the buffer in bytes.</summary>
        [Key(1)]
        public ulong Size;

        /// <summary>The descriptor set it is bound in.</summary>
        [Key(2)]
        public uint Set;

        /// <summary>Its binding in the set.</summary>
        [Key(3)]
        public uint Binding;

        /// <summary>The values in the buffer.</summary>
        [Key(4)]
        public List<ValueTypeParameter> Parameters;

        public bool Equals(ConstantBuffer other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Name, other.Name) && Size == other.Size && Set == other.Set && Binding == other.Binding
                   && Utilities.Compare(Parameters, other.Parameters);
        }

        public override bool Equals(object obj) => obj is ConstantBuffer other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Name.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Size;
                hashCode = (hashCode * 397) ^ (int)Set;
                hashCode = (hashCode * 397) ^ (int)Binding;
                hashCode = (hashCode * 397) ^ Parameters.Count;
                return hashCode;
            }
        }

        public static bool operator ==(ConstantBuffer left, ConstantBuffer right) => Equals(left, right);

        public static bool operator !=(ConstantBuffer left, ConstantBuffer right) => !Equals(left, right);
    }
}
