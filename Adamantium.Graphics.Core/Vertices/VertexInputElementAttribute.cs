using System;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>Marks a field or property of a vertex structure as one vertex element.</summary>
/// <seealso cref="VertexInputElement"/>
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property)]
public class VertexInputElementAttribute :Attribute
{
   /// <summary>The semantic the shader knows the element by.</summary>
   public String SemanticName { get; private set; }

   /// <summary>Which of the elements of one semantic this is - the rows of a matrix are four, 0 to 3.</summary>
   public Int32 SemanticIndex { get; private set; }

   /// <summary>The element's data format; <see cref="Format.UNDEFINED"/> to take it from the field's type.</summary>
   public Format Format { get; private set; }

   /// <summary>The element's offset in bytes, -1 to place it right after the one before.</summary>
   public Int32 BytesOffset { get; private set; }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexInputElement" /> struct.
   /// </summary>
   /// <param name="semanticName">Name of the semantic.</param>
   /// <remarks>
   /// If the semantic name contains a postfix number, this number will be used as a semantic index. 
   /// The <see cref="Adamantium.Vulkan.Core"/> will be mapped from the field type.
   /// </remarks>
   public VertexInputElementAttribute(String semanticName)
   {
      ParseSemantic(semanticName.ToUpper(), 0);
      Format = Format.UNDEFINED;
      BytesOffset = -1;
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexInputElement" /> struct.
   /// </summary>
   /// <param name="semanticName">Name of the semantic.</param>
   /// <param name="format">The format.</param>
   /// <remarks>
   /// If the semantic name contains a postfix number, this number will be used as a semantic index.
   /// </remarks>
   public VertexInputElementAttribute(String semanticName, Format format)
   {
      ParseSemantic(semanticName.ToUpper(), 0);
      Format = format;
      BytesOffset = -1;
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexInputElement" /> struct.
   /// </summary>
   /// <param name="semanticName">Name of the semantic.</param>
   /// <param name="semanticIndex">Index of the semantic.</param>
   /// <param name="format">The format.</param>
   /// <param name="bytesOffset">The aligned byte offset.</param>
   public VertexInputElementAttribute(String semanticName, Int32 semanticIndex, Format format, Int32 bytesOffset = -1)
   {
      ParseSemantic(semanticName.ToUpper(), semanticIndex);
      Format = format;
      BytesOffset = bytesOffset;
   }

   private void ParseSemantic(String semanticName, Int32 semanticIndex)
   {
      var match = VertexInputElement.MatchSemanticIndex.Match(semanticName);
      if (match.Success)
      {
         SemanticName = match.Groups[1].Value;
         SemanticIndex = Int32.Parse(match.Groups[2].Value);
      }
      else
      {
         SemanticName = semanticName.ToUpper();
         SemanticIndex = semanticIndex;
      }
   }
      
}