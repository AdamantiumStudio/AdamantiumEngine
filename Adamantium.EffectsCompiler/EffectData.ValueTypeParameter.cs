using System;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public partial class EffectData
{
    /// <summary>A value in a uniform buffer: where it is, how large, and its shape.</summary>
    [MessagePackObject]
    public sealed class ValueTypeParameter : Parameter, IEquatable<ValueTypeParameter>
    {
        /// <summary>The offset of the value in its buffer, in bytes.</summary>
        [Key(3)]
        public uint Offset;

        /// <summary>The number of elements: 1 for a value that is no array.</summary>
        [Key(4)]
        public uint Count;

        /// <summary>The size of the value in bytes.</summary>
        [Key(5)]
        public int Size;

        /// <summary>The rows of a vector or a matrix; 1 for a scalar.</summary>
        [Key(6)]
        public byte RowCount;

        /// <summary>The columns of a matrix; 1 for a scalar or a vector.</summary>
        [Key(7)]
        public byte ColumnCount;

        public bool Equals(ValueTypeParameter other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return base.Equals(other) && Offset == other.Offset && Count == other.Count && Size == other.Size
                   && RowCount == other.RowCount && ColumnCount == other.ColumnCount;
        }

        public override bool Equals(object obj) => obj is ValueTypeParameter other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Offset;
                hashCode = (hashCode * 397) ^ (int)Count;
                hashCode = (hashCode * 397) ^ Size;
                hashCode = (hashCode * 397) ^ RowCount;
                hashCode = (hashCode * 397) ^ ColumnCount;
                return hashCode;
            }
        }

        public static bool operator ==(ValueTypeParameter left, ValueTypeParameter right) => Equals(left, right);

        public static bool operator !=(ValueTypeParameter left, ValueTypeParameter right) => !Equals(left, right);
    }
}
