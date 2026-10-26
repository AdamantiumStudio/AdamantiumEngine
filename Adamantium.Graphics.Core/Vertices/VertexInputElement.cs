using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Adamantium.Core;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Vertices;

public struct VertexInputElement : IEquatable<VertexInputElement>
{
   public String SemanticName { get; private set; }
   public Int32 SemanticIndex { get; private set; }
   public Format Format { get; private set; }
   public Int32 AlignedByteOffset { get; internal set; }

   private int hashCode;

   /// <summary>
   ///   Returns a value that can be used for the offset parameter of an InputElement to indicate that the element
   ///   should be aligned directly after the previous element, including any packing if necessary.
   /// </summary>
   /// <returns>A value used to align input elements.</returns>
   public const int AppendAligned = -1;

   internal static readonly Regex MatchSemanticIndex = new Regex(@"(.*)(\d+)$");

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexInputElement" /> struct.
   /// </summary>
   /// <param name="semanticName">Name of the semantic.</param>
   /// <param name="format">The format.</param>
   /// /// <param name="alignedByteOffset">The aligned byte offset.</param>
   /// <remarks>
   /// If the semantic name contains a postfix number, this number will be used as a semantic index.
   /// </remarks>
   public VertexInputElement(string semanticName, Format format, int alignedByteOffset = AppendAligned) :this()
   {
      if (semanticName == null)
         throw new ArgumentNullException(nameof(semanticName));

      semanticName = semanticName.ToUpperInvariant();

      var match = MatchSemanticIndex.Match(semanticName);
      if (match.Success)
      {
         SemanticName = match.Groups[1].Value;
         SemanticIndex = int.Parse(match.Groups[2].Value);
      }
      else
      {
         SemanticName = semanticName;
      }

      Format = format;
      AlignedByteOffset = alignedByteOffset;

      hashCode = CalculateHashCode();
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexInputElement" /> struct.
   /// </summary>
   /// <param name="semanticName">Name of the semantic.</param>
   /// <param name="semanticIndex">Index of the semantic.</param>
   /// <param name="format">The format.</param>
   /// <param name="alignedByteOffset">The aligned byte offset.</param>
   public VertexInputElement(string semanticName, int semanticIndex, Format format,
      int alignedByteOffset = AppendAligned) : this()
   {
      if (semanticName == null)
         throw new ArgumentNullException(nameof(semanticName));

      semanticName = semanticName.ToUpperInvariant();

      var match = MatchSemanticIndex.Match(semanticName);
      if (match.Success)
         throw new ArgumentException(
            "Semantic name cannot contain a semantic index when using constructor with explicit semantic index. Use implicit semantic index constructor.");

      SemanticName = semanticName;
      SemanticIndex = semanticIndex;
      Format = format;
      AlignedByteOffset = alignedByteOffset;

      hashCode = CalculateHashCode();
   }

   private int CalculateHashCode()
   {
      unchecked
      {
         int localHashCode = SemanticName.GetHashCode();
         localHashCode = (localHashCode * 397) ^ SemanticIndex;
         localHashCode = (localHashCode * 397) ^ Format.GetHashCode();
         localHashCode = (localHashCode * 397) ^ AlignedByteOffset;
         return localHashCode;
      }
   }

   public bool Equals(VertexInputElement other)
   {
      return hashCode == other.hashCode && SemanticName.Equals(other.SemanticName) && SemanticIndex == other.SemanticIndex && Format == other.Format && AlignedByteOffset == other.AlignedByteOffset;
   }

   public override bool Equals(object obj)
   {
      if (ReferenceEquals(null, obj)) return false;
      return obj is VertexInputElement && Equals((VertexInputElement)obj);
   }

   public override int GetHashCode()
   {
      return hashCode;
   }

   public static bool operator ==(VertexInputElement left, VertexInputElement right)
   {
      return left.Equals(right);
   }

   public static bool operator !=(VertexInputElement left, VertexInputElement right)
   {
      return !left.Equals(right);
   }

   public override string ToString()
   {
      return $"{SemanticName}{(SemanticIndex == 0 ? string.Empty : string.Empty + SemanticIndex)},{Format},{AlignedByteOffset}";
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "COLOR".
   /// </summary>
   /// <typeparam name="T">Type of the Color semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Color<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return Color(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "COLOR".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Color(Format format, int offsetInBytes = AppendAligned)
   {
      return Color(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "COLOR".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Color(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("COLOR", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "NORMAL".
   /// </summary>
   /// <typeparam name="T">Type of the Normal semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Normal<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return Normal(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "NORMAL".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Normal(Format format, int offsetInBytes = AppendAligned)
   {
      return Normal(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "NORMAL".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Normal(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("NORMAL", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDINDICES".
   /// </summary>
   /// <typeparam name="T">Type of the BlendIndices semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendIndices<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return BlendIndices(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDINDICES".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendIndices(Format format, int offsetInBytes = AppendAligned)
   {
      return BlendIndices(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDINDICES".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendIndices(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("BLENDINDICES", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDWEIGHT".
   /// </summary>
   /// <typeparam name="T">Type of the BlendWeights semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendWeights<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return BlendWeights(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDWEIGHT".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendWeights(Format format, int offsetInBytes = AppendAligned)
   {
      return BlendWeights(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BLENDWEIGHT".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BlendWeights(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("BLENDWEIGHT", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "POSITION".
   /// </summary>
   /// <typeparam name="T">Type of the Position semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Position<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return Position(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "POSITION".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Position(Format format, int offsetInBytes = AppendAligned)
   {
      return Position(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "POSITION".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Position(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("POSITION", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "SV_POSITION".
   /// </summary>
   /// <typeparam name="T">Type of the PositionTransformed semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement PositionTransformed<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return PositionTransformed(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "SV_POSITION".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement PositionTransformed(Format format, int offsetInBytes = AppendAligned)
   {
      return PositionTransformed(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "SV_POSITION".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement PositionTransformed(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("SV_POSITION", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TEXCOORD".
   /// </summary>
   /// <typeparam name="T">Type of the TextureCoordinate semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement TextureCoordinate<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return TextureCoordinate(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TEXCOORD".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement TextureCoordinate(Format format, int offsetInBytes = AppendAligned)
   {
      return TextureCoordinate(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TEXCOORD".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement TextureCoordinate(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("TEXCOORD", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TANGENT".
   /// </summary>
   /// <typeparam name="T">Type of the Tangent semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Tangent<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return Tangent(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TANGENT".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Tangent(Format format, int offsetInBytes = AppendAligned)
   {
      return Tangent(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "TANGENT".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement Tangent(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("TANGENT", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BITANGENT".
   /// </summary>
   /// <typeparam name="T">Type of the BiTangent semantic.</typeparam>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BiTangent<T>(int semanticIndex = 0, int offsetInBytes = AppendAligned) where T : struct
   {
      return BiTangent(semanticIndex, ConvertTypeToFormat<T>(), offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BITANGENT".
   /// </summary>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BiTangent(Format format, int offsetInBytes = AppendAligned)
   {
      return BiTangent(0, format, offsetInBytes);
   }

   /// <summary>
   /// Declares a VertexInputElement with the semantic "BITANGENT".
   /// </summary>
   /// <param name="semanticIndex">The semantic index.</param>
   /// <param name="format">Format of this element.</param>
   /// <param name="offsetInBytes">The offset in bytes of this element. Use <see cref="AppendAligned"/> to compute automatically the offset from previous elements.</param>
   /// <returns>A new instance of <see cref="VertexInputElement" /> that represents this semantic.</returns>
   public static VertexInputElement BiTangent(int semanticIndex, Format format, int offsetInBytes = AppendAligned)
   {
      return new VertexInputElement("BITANGENT", semanticIndex, format, offsetInBytes);
   }

   /// <summary>
   /// Extracts a set of <see cref="VertexInputElement"/> defined from a type that is using <see cref="VertexInputElementAttribute"/>.
   /// </summary>
   /// <typeparam name="T">Type of the class to inspect for <see cref="VertexInputElementAttribute"/>.</typeparam>
   /// <returns>An array of <see cref="VertexInputElement"/>.</returns>
   public static VertexInputElement[] FromType<T>() where T : struct
   {
      return FromType(typeof(T));
   }

   /// <summary>
   /// Extracts a set of <see cref="VertexInputElement"/> defined from a type that is using <see cref="VertexInputElementAttribute"/>.
   /// </summary>
   /// <param name="type">The Type of the class to inspect for <see cref="VertexInputElementAttribute"/>.</param>
   /// <returns>An array of <see cref="VertexInputElement"/>.</returns>
   /// <exception cref="System.ArgumentNullException">If type is null.</exception>
   /// <exception cref="System.ArgumentException">If type doesn't contain any <see cref="VertexInputElementAttribute"/></exception>
   public static VertexInputElement[] FromType(Type type)
   {
      if (type == null)
         throw new ArgumentNullException(nameof(type));

      if (!type.IsValueType)
         throw new ArgumentException("Type must be a value type");

      var vertexElements = new List<VertexInputElement>();
      foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
      {
         var attributes = field.GetCustomAttributes<VertexInputElementAttribute>();
         foreach (var vertexElementAttribute in attributes)
         {
            var fieldFormat = vertexElementAttribute.Format;
            if (fieldFormat == Format.UNDEFINED)
               fieldFormat = GetFormatFromType(field.FieldType);

            var offset = vertexElementAttribute.BytesOffset;
            if (offset < 0)
               offset = Marshal.OffsetOf(type, field.Name).ToInt32();

            vertexElements.Add(new VertexInputElement(vertexElementAttribute.SemanticName, fieldFormat, offset));
            break;
         }
      }

      if (vertexElements.Count == 0) return null;

      return vertexElements.ToArray();
   }


   private static Format ConvertTypeToFormat<T>() where T : struct
   {
      return GetFormatFromType(typeof(T));
   }

   /// <summary>
   /// Converts a type to a <see cref="Format"/>.
   /// </summary>
   /// <param name="typeT">The type T.</param>
   /// <returns>The equivalent Format.</returns>
   /// <exception cref="System.NotSupportedException">If the conversion for this type is not supported.</exception>
   public static Format GetFormatFromType(Type typeT)
   {
      if (typeof(Vector4F) == typeT || typeof(Color4F) == typeT || typeof(RectangleF) == typeT)
         return Format.R32G32B32A32_SFLOAT;
      if (typeof(Vector3F) == typeT || typeof(Color3F) == typeT)
         return Format.R32G32B32_SFLOAT;
      if (typeof(Vector2F) == typeT)
         return Format.R32G32_SFLOAT;
      if (typeof(float) == typeT)
         return Format.R32_SFLOAT;

      if (typeof(Color) == typeT)
         return Format.R8G8B8A8_UNORM;
      if (typeof(ColorBGRA) == typeT)
         return Format.B8G8R8A8_UNORM;

      if (typeof(Int4) == typeT)
         return Format.R32G32B32A32_UINT;
      if (typeof(Int3) == typeT)
         return Format.R32G32B32_UINT;
      if (typeof(int) == typeT)
         return Format.R32_SINT;
      if (typeof(uint) == typeT)
         return Format.R32_UINT;

      if (typeof(Bool4) == typeT)
         return Format.R32G32B32A32_UINT;

      throw new NotSupportedException($"Type [{typeT.Name}] is not supported. You must specify an explicit DXGI.Format");
   }
}