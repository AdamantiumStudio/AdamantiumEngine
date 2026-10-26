using System;
using System.Collections.ObjectModel;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>	
/// A description of a vertex elements for particular slot for the input-assembler stage. 
/// This structure is related to <see cref="VertexInputElement"/>.
/// </summary>	
/// <remarks>	
/// Because <see cref="VertexInputElement"/> requires to have the same <see cref="SlotIndex"/>, <see cref="InstanceCount"/>,
/// this <see cref="VertexBufferLayout"/> structure encapsulates a set of <see cref="VertexInputElement"/> for a particular slot, instance count.
/// </remarks>	
/// <seealso cref="VertexInputElement"/>
public class VertexBufferLayout : IEquatable<VertexBufferLayout>
{
   /// <summary>
   /// Vertex buffer slot index.
   /// </summary>
   public readonly int SlotIndex;

   /// <summary>How many instances share one element of per-instance data; 0 for per-vertex data.</summary>
   public readonly int InstanceCount;

   /// <summary>
   /// Vertex elements describing this declaration.
   /// </summary>
   public readonly ReadOnlyCollection<VertexInputElement> VertexElements;

   private int hashCode;

   private VertexBufferLayout(int slot, VertexInputElement[] elements, int instanceCount)
   {
      if (elements == null)
         throw new ArgumentNullException(nameof(elements));

      if (elements.Length == 0)
         throw new ArgumentException("Vertex elements cannot have zero elements.", nameof(elements));

      var copyElements = new VertexInputElement[elements.Length];
      Array.Copy(elements, copyElements, elements.Length);

      SlotIndex = slot;
      VertexElements = new ReadOnlyCollection<VertexInputElement>(CalculateStaticOffsets(copyElements));
      InstanceCount = instanceCount;

      ComputeHashcode();
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexBufferLayout" /> struct.
   /// </summary>
   /// <param name="slot">The slot to bind this vertex buffer to.</param>
   /// <param name="structType">Type of a structure that is using <see cref="VertexInputElementAttribute" />.</param>
   /// <param name="instanceCount">Specify the instancing count. Set to 0 for no instancing.</param>
   /// <returns>A new instance of <see cref="VertexBufferLayout"/>.</returns>
   public static VertexBufferLayout New(int slot, Type structType, int instanceCount = 0)
   {
      var vertexElements = VertexInputElement.FromType(structType);
      if (vertexElements == null)
      {
         throw new ArgumentException(
            $"Unable to calculate VertexElements from Type [{structType.Name}]. This type is not using VertexInputElementAttribute.", nameof(structType));
      }

      return New(slot, vertexElements, instanceCount);
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexBufferLayout" /> struct.
   /// </summary>
   /// <typeparam name="T">Type of a structure that is using <see cref="VertexInputElementAttribute" />.</typeparam>
   /// <param name="slot">The slot to bind this vertex buffer to.</param>
   /// <param name="instanceCount">Specify the instancing count. Set to 0 for no instancing.</param>
   /// <returns>A new instance of <see cref="VertexBufferLayout"/>.</returns>
   public static VertexBufferLayout New<T>(int slot, int instanceCount = 0) where T : struct
   {
      return New(slot, typeof(T), instanceCount);
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexBufferLayout" /> struct.
   /// </summary>
   /// <param name="slot">The slot to bind this vertex buffer to.</param>
   /// <param name="elements">The elements.</param>
   /// <returns>A new instance of <see cref="VertexBufferLayout"/>.</returns>
   public static VertexBufferLayout New(int slot, params VertexInputElement[] elements)
   {
      return New(slot, elements, 0);
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="VertexBufferLayout" /> struct with instantiated data.
   /// </summary>
   /// <param name="slot">The slot to bind this vertex buffer to.</param>
   /// <param name="elements">The elements.</param>
   /// <param name="instanceCount">Specify the instancing count. Set to 0 for no instancing.</param>
   /// <returns>A new instance of <see cref="VertexBufferLayout"/>.</returns>
   public static VertexBufferLayout New(int slot, VertexInputElement[] elements, int instanceCount)
   {
      return new VertexBufferLayout(slot, elements, instanceCount);
   }

   private static VertexInputElement[] CalculateStaticOffsets(VertexInputElement[] vertexElements)
   {
      int offset = 0;
      for (int i = 0; i < vertexElements.Length; i++)
      {
         if (vertexElements[i].AlignedByteOffset == -1)
            vertexElements[i].AlignedByteOffset = offset;
         else
            offset = vertexElements[i].AlignedByteOffset;

         offset += FormatHelper.SizeOfInBytes(vertexElements[i].Format);
      }
      return vertexElements;
   }

   public bool Equals(VertexBufferLayout other)
   {
      if (ReferenceEquals(null, other)) return false;
      if (ReferenceEquals(this, other)) return true;
      return hashCode == other.hashCode && VertexElements.Equals(other.VertexElements) && InstanceCount == other.InstanceCount;
   }

   public override bool Equals(object obj)
   {
      if (ReferenceEquals(null, obj)) return false;
      if (ReferenceEquals(this, obj)) return true;
      if (obj.GetType() != this.GetType()) return false;
      return Equals((VertexBufferLayout)obj);
   }

   public override int GetHashCode()
   {
      return hashCode;
   }

   private void ComputeHashcode()
   {
      hashCode = InstanceCount;
      hashCode = (hashCode * 397) ^ VertexElements.GetHashCode();
   }

   public static bool operator ==(VertexBufferLayout left, VertexBufferLayout right)
   {
      return Equals(left, right);
   }

   public static bool operator !=(VertexBufferLayout left, VertexBufferLayout right)
   {
      return !Equals(left, right);
   }
}