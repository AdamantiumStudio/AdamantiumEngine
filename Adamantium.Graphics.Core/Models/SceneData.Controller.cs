using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>A skin: the joints a mesh's vertices are bound to - nodes of the model - and how the mesh stood against
   /// them when it was bound.</summary>
   public class Controller
   {
      public Controller()
      {
         ControllerId = String.Empty;
         Name = String.Empty;
         MeshId = String.Empty;
         JointNames = new List<String>();
         JointMatrices = new List<Matrix4x4F>();
         BoneIndices = new List<Vector4F>();
         BoneWeights = new List<Vector4F>();
      }

      public String ControllerId { get; set; }

      public String MeshId { get; set; }

      public String Name { get; set; }

      public List<Vector4F> BoneIndices { get; set; }

      public List<Vector4F> BoneWeights { get; set; }

      /// <summary>The joints, in the order a vertex's bone indices count them: the <see cref="Model.ID"/>s of the
      /// nodes they are.</summary>
      public List<String> JointNames { get; set; }

      /// <summary>Each joint's inverse bind matrix, in the order of <see cref="JointNames"/>: from the mesh as it was
      /// bound into the joint's own space.</summary>
      public List<Matrix4x4F> JointMatrices { get; set; }

      /// <summary>The ID of the node the source file names the skeleton's root; null when it names none.</summary>
      public String SkeletonRoot { get; set; }

      /// <summary>What the source file says of the skin beyond the rest.</summary>
      public ExtraData Extra { get; set; }
   }
}
