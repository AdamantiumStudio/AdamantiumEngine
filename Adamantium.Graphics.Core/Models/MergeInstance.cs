using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public class MergeInstance
{
   public Mesh Mesh { get; set; }

   public Matrix4x4<double> Transform { get; set; }

   public bool ApplyTransform { get; set; }

   public MergeInstance() { }

   public MergeInstance(Mesh mesh, Matrix4x4<double> transform, bool applyTransform)
   {
      Mesh = mesh;
      Transform = transform;
      ApplyTransform = applyTransform;
   }
}