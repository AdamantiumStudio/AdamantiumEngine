using System;
using MessagePack;

namespace Adamantium.EffectsCompiler
{
   public partial class EffectData
   {
      /// <summary>One parameter of a shader's input or output signature: its semantic, where its data lives and which
      /// components it uses.</summary>
      [MessagePackObject]
      public sealed class Semantic : IEquatable<Semantic>
      {
         /// <summary>An empty semantic, for the serializer.</summary>
         public Semantic()
         {
         }

         /// <summary>A semantic of all its fields.</summary>
         public Semantic(string name, byte index, byte register, byte systemValueType, byte componentType,
            byte usageMask, byte readWriteMask, byte stream)
         {
            Name = name;
            Index = index;
            Register = register;
            SystemValueType = systemValueType;
            ComponentType = componentType;
            UsageMask = usageMask;
            ReadWriteMask = readWriteMask;
            Stream = stream;
         }

         /// <summary>The semantic's name.</summary>
         [Key(0)]
         public string Name { get; internal set; }

         /// <summary>Which of the parameters of one semantic name this is.</summary>
         [Key(1)]
         public byte Index { get; internal set; }

         /// <summary>The register its data is in.</summary>
         [Key(2)]
         public byte Register { get; internal set; }

         /// <summary>The system value it is, if any.</summary>
         [Key(3)]
         public byte SystemValueType { get; internal set; }

         /// <summary>The type of each component in the register.</summary>
         [Key(4)]
         public byte ComponentType { get; internal set; }

         /// <summary>Which components of the register it uses, a bit each.</summary>
         [Key(5)]
         public byte UsageMask { get; internal set; }

         /// <summary>Which components are always read (input) or never written (output), a bit each.</summary>
         [Key(6)]
         public byte ReadWriteMask { get; internal set; }

         /// <summary>The geometry shader stream it belongs to.</summary>
         [Key(7)]
         public byte Stream { get; internal set; }

         public bool Equals(Semantic other) =>
            other is not null && (ReferenceEquals(this, other)
                                  || Name == other.Name && Index == other.Index && Register == other.Register
                                  && SystemValueType == other.SystemValueType
                                  && ComponentType == other.ComponentType && UsageMask == other.UsageMask
                                  && ReadWriteMask == other.ReadWriteMask && Stream == other.Stream);

         public override bool Equals(object obj) => obj is Semantic other && Equals(other);

         public override int GetHashCode()
         {
            unchecked
            {
               var hash = Name?.GetHashCode() ?? 0;
               foreach (var value in new[] { Index, Register, SystemValueType, ComponentType, UsageMask, ReadWriteMask, Stream })
               {
                  hash = hash * 31 + value;
               }

               return hash;
            }
         }

         public static bool operator ==(Semantic left, Semantic right) => Equals(left, right);

         public static bool operator !=(Semantic left, Semantic right) => !Equals(left, right);
      }
   }
}
