using System;

namespace Adamantium.Mathematics
{
   /// <summary>
   /// The bounds of something in a world kept in double: a box turned by <see cref="Orientation"/> around
   /// <see cref="Center"/>, and the radius of the sphere fitted inside it. Made float - a box, an oriented box, a
   /// sphere - only on request, as that loses precision far from the origin.
   /// </summary>
   public struct Bounds
   {
      /// <summary>The middle.</summary>
      public Vector3<double> Center;

      /// <summary>Half the size along the bounds' own axes.</summary>
      public Vector3<double> HalfExtent;

      /// <summary>The largest half extent: the radius of a ball whose bounds these are, the box's corners left
      /// out.</summary>
      public double Radius;

      /// <summary>The turn from the world's axes to the bounds' own.</summary>
      public Quaternion<double> Orientation;

      /// <summary>Bounds around <paramref name="center"/>, <paramref name="halfExtents"/> to each side, turned by
      /// <paramref name="rotation"/>.</summary>
      public Bounds(Vector3<double> center, Vector3<double> halfExtents, Quaternion<double> rotation)
      {
         Center = center;
         HalfExtent = halfExtents;
         Radius = Vector3<double>.Max(halfExtents);
         Orientation = rotation;
      }

      /// <summary>The full size along the bounds' own axes.</summary>
      public readonly Vector3<double> Size => HalfExtent * 2;

      /// <summary>The size along X.</summary>
      public readonly double Width => Size.X;

      /// <summary>The size along Y.</summary>
      public readonly double Height => Size.Y;

      /// <summary>The size along Z.</summary>
      public readonly double Depth => Size.Z;

      /// <summary>Twice <see cref="Radius"/>.</summary>
      public readonly double Diameter => Radius * 2;

      /// <summary>The smallest bounds along the world's axes holding every one of <paramref name="points"/>.</summary>
      /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
      public static Bounds FromPoints(ReadOnlySpan<Vector3<double>> points)
      {
         var box = BoundingBox.CreateFromPoints(points);
         var center = (box.Minimum + box.Maximum) / 2;
         return new Bounds(center, box.Maximum - center, Quaternion<double>.Identity);
      }

      /// <summary>The bounds scaled by <paramref name="scale"/> along the world's axes about the origin, turned by
      /// <paramref name="rotation"/> around it, then moved by <paramref name="translation"/> - as
      /// <see cref="OrientedBoundingBox{T}.Transform(Vector3{T}, Quaternion{T}, Vector3{T})"/> carries a box.</summary>
      public readonly Bounds Transform(Vector3<double> scale, Quaternion<double> rotation, Vector3<double> translation) =>
         FromBoundingBox(ToOrientedBoundingBox().Transform(scale, rotation, translation));

      /// <summary>Copies the corners to <paramref name="corners"/>, in the order of
      /// <see cref="OrientedBoundingBox{T}.GetCorners"/>.</summary>
      /// <exception cref="ArgumentOutOfRangeException"><paramref name="corners"/> holds fewer than eight.</exception>
      public readonly void GetCorners(Span<Vector3<double>> corners) => ToOrientedBoundingBox().GetCorners(corners);

      /// <summary>The box along the world's axes of <see cref="Center"/> and <see cref="HalfExtent"/>, the
      /// orientation left out.</summary>
      public readonly BoundingBox<double> ToBoundingBox() => new(Center - HalfExtent, Center + HalfExtent);

      /// <summary>The oriented box of the same center, half extent and orientation.</summary>
      public readonly OrientedBoundingBox<double> ToOrientedBoundingBox() => new(Center, HalfExtent, Orientation);

      /// <summary>The sphere of <see cref="Center"/> and <see cref="Radius"/>.</summary>
      public readonly BoundingSphere<double> ToBoundingSphere() => new(Center, Radius);

      /// <summary>The bounds of <paramref name="box"/>.</summary>
      public static Bounds FromBoundingBox(OrientedBoundingBox<double> box) => new(box.Center, box.HalfExtent, box.Orientation);

      /// <summary>The bounds of <paramref name="sphere"/>: the cube around it.</summary>
      public static Bounds FromBoundingSphere(BoundingSphere<double> sphere) =>
         new(sphere.Center, new Vector3<double>(sphere.Radius), Quaternion<double>.Identity);

      /// <inheritdoc />
      public override readonly string ToString() =>
         $"Center: {Center}, HalfExtent: {HalfExtent}, Radius: {Radius}, Orientation: {Orientation}";
   }
}
