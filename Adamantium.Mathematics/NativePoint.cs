using System;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics
{
   /// <summary>
   /// Describes a point
   /// </summary>
   [StructLayout(LayoutKind.Sequential)]
   public struct NativePoint
   {
      /// <summary>
      /// X coordinate
      /// </summary>
      public Int32 X { get; set; }

      /// <summary>
      /// Y coordinate
      /// </summary>
      public Int32 Y { get; set; }

      /// <summary>
      /// Creates a point at <paramref name="x"/>, <paramref name="y"/>.
      /// </summary>
      /// <param name="x">The X coordinate.</param>
      /// <param name="y">The Y coordinate.</param>
      public NativePoint(Int32 x, Int32 y)
      {
         X = x;
         Y = y;
      }

      /// <summary>
      /// Converts a <see cref="NativePoint"/> to a <see cref="Vector2{T}"/> of doubles.
      /// </summary>
      /// <param name="point">The point to convert.</param>
      /// <returns>The same point as a vector.</returns>
      public static implicit operator Vector2<double>(NativePoint point)
      {
         return new Vector2<double>(point.X, point.Y);
      }
   }
}
