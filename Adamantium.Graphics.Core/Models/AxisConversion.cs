using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>
/// The basis change between the import's coordinate systems. The very same one
/// <see cref="Mesh.ChangeCoordinateSystem"/> applies to vertices - and that is the whole point: the mesh was
/// converted while the skeleton and the animation key frames were not, so in a Y_UP file the bones came out
/// flipped relative to the model they belong to.
/// </summary>
public static class AxisConversion
{
   /// <summary>The matrix that moves a point from one system to another. Identity for a pair we do not support -
   /// "change nothing" rather than quietly mangle it.</summary>
   public static Matrix4x4F Between(UpAxis from, UpAxis to)
   {
      if (from == to) return Matrix4x4F.Identity;

      switch (from)
      {
         case UpAxis.Z_UP:
            switch (to)
            {
               //(x, z, -y)
               case UpAxis.Y_UP_RH: return Axes(1, 0, 0, 0, 0, -1, 0, 1, 0);
               //(x, z, y)
               case UpAxis.Y_UP_LH:
               case UpAxis.Y_DOWN_RH: return Axes(1, 0, 0, 0, 0, 1, 0, 1, 0);
            }
            break;

         case UpAxis.Y_UP_RH:
            switch (to)
            {
               //(x, y, -z)
               case UpAxis.Y_UP_LH: return Axes(1, 0, 0, 0, 1, 0, 0, 0, -1);
               //(x, -y, z)
               case UpAxis.Y_DOWN_RH: return Axes(1, 0, 0, 0, -1, 0, 0, 0, 1);
            }
            break;

         case UpAxis.Y_UP_RH_FRONT_Z:
            switch (to)
            {
               //(-x, -y, -z): the front turns to -Z, where the engine's models face
               case UpAxis.Y_DOWN_RH: return Axes(-1, 0, 0, 0, -1, 0, 0, 0, -1);
            }
            break;
      }

      return Matrix4x4F.Identity;
   }

   /// <summary>Whether the change turns the world inside out. A swap of two axes mirrors it and a triangle's
   /// winding has to be reversed to keep facing the same way; a plain rotation does not, and reversing there
   /// leaves every face pointing backwards.</summary>
   public static bool MirrorsHandedness(Matrix4x4F conversion) => conversion.Determinant() < 0;

   /// <summary>Moves a transform (a bone matrix, a node pose) into the new system: <c>M⁻¹ * T * M</c>. The engine
   /// multiplies a row vector by a matrix, hence that order; the inverse of an orthonormal matrix is its
   /// transpose. For axis swaps the order makes no difference, but for a real rotation (Z_UP -> Y_UP_RH) getting
   /// it backwards skewed the result - which is what AxisConversionTests catches.</summary>
   public static Matrix4x4F Convert(Matrix4x4F transform, Matrix4x4F conversion)
   {
      return Matrix4x4F.Transpose(conversion) * transform * conversion;
   }

   //Rows say where each source axis goes: the engine multiplies a row vector by the matrix
   private static Matrix4x4F Axes(float xx, float xy, float xz, float yx, float yy, float yz,
      float zx, float zy, float zz)
   {
      var matrix = Matrix4x4F.Identity;
      matrix.M11 = xx; matrix.M12 = xy; matrix.M13 = xz;
      matrix.M21 = yx; matrix.M22 = yy; matrix.M23 = yz;
      matrix.M31 = zx; matrix.M32 = zy; matrix.M33 = zz;
      return matrix;
   }
}