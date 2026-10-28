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
      /// Creates new instance of <see cref="Point"/>
      /// </summary>
      /// <param name="x"></param>
      /// <param name="y"></param>
      public NativePoint(Int32 x, Int32 y)
      {
         X = x;
         Y = y;
      }

      /// <summary>
      /// Convert <see cref="Point"/> to a <see cref="Point"/>
      /// </summary>
      /// <param name="point">point to convert</param>
      /// <returns>returns new <see cref="Point"/></returns>
      public static implicit operator Vector2<double>(NativePoint point)
      {
         return new Vector2<double>(point.X, point.Y);
      }
   }
}
