using System;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>Marks a field of a vertex structure as one input of the vertex shader. The fields are the inputs in the
/// order they are declared, each at its place in the structure.</summary>
[AttributeUsage(AttributeTargets.Field)]
public class VertexInputElementAttribute : Attribute
{
   /// <summary>The input the field is, by the semantic the shader names it with.</summary>
   public String SemanticName { get; }

   /// <summary>The format the field is read in; <see cref="Format.UNDEFINED"/> takes it from the field's type.</summary>
   public Format Format { get; }

   public VertexInputElementAttribute(String semanticName, Format format = Format.UNDEFINED)
   {
      SemanticName = semanticName ?? throw new ArgumentNullException(nameof(semanticName));
      Format = format;
   }
}
