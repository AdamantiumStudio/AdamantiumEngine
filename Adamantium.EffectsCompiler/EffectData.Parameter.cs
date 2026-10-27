using System;
using MessagePack;

namespace Adamantium.EffectsCompiler
{
   public partial class EffectData
   {
      [MessagePackObject]
      public abstract class Parameter : IEquatable<Parameter>
      {
         /// <summary>
         /// Name of this parameter.
         /// </summary>
         [Key(0)]
         public string Name;

         /// <summary>
         /// The <see cref="EffectParameterClass"/> of this parameter.
         /// </summary>
         [Key(1)]
         public EffectParameterClass Class;

         /// <summary>
         /// The <see cref="EffectParameterType"/> of this parameter.
         /// </summary>
         [Key(2)]
         public EffectParameterType Type;

         public bool Equals(Parameter other)
         {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Name, other.Name) && Class == other.Class && Type == other.Type;
         }

         public override bool Equals(object obj)
         {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Parameter)obj);
         }

         public override int GetHashCode()
         {
            unchecked
            {
               int hashCode = Name.GetHashCode();
               hashCode = (hashCode * 397) ^ Class.GetHashCode();
               hashCode = (hashCode * 397) ^ Type.GetHashCode();
               return hashCode;
            }
         }

         public static bool operator ==(Parameter left, Parameter right)
         {
            return Equals(left, right);
         }

         public static bool operator !=(Parameter left, Parameter right)
         {
            return !Equals(left, right);
         }

         /// <inheritdoc/>
         public override string ToString()
         {
            return $"Name: {Name}, Class: {Class}, Type: {Type}";
         }
      }
   }
}
