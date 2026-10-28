using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Converter.Containers
{
   public class ObjGeometryData
   {
      public ObjGeometryData()
      {
      }

      public List<Vector3<double>> Positions { get; internal set; }
      public List<Vector3F> Normals { get; internal set; }
      public List<Vector2F> UV { get; internal set; }

   }
}
