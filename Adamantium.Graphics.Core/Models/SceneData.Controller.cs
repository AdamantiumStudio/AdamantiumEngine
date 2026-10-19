using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class Controller
   {
      /// <summary>How many joints of one skin move its vertices when it is drawn; joints past them do not.</summary>
      public const int MaxDrawnJoints = 128;

      public Controller()
      {
         BindShapeMatrix = Matrix4x4F.Identity;
         ControllerId = String.Empty;
         Name = String.Empty;
         MeshId = String.Empty;
         JointDictionary = new Dictionary<String, Matrix4x4F>();
         JointNames = new List<String>();
         JointMatrices = new List<Matrix4x4F>();
         BoneIndices = new List<Vector4F>();
         BoneWeights = new List<Vector4F>();
      }

      public Matrix4x4F BindShapeMatrix { get; set; }

      public String ControllerId { get; set; }

      public String MeshId { get; set; }

      public String Name { get; set; }

      public Dictionary<String, Matrix4x4F> JointDictionary { get; set; }

      public List<Vector4F> BoneIndices { get; set; }
         
      public List<Vector4F> BoneWeights { get; set; } 

      public List<String> JointNames { get; set; }

      public List<Matrix4x4F> JointMatrices { get; set; }

      /// <summary>Whether the joints are nodes of the model - <see cref="JointNames"/> their <see cref="Model.ID"/>s, in a
      /// vertex's order, <see cref="JointMatrices"/> their inverse bind matrices - as glTF has them, rather than a
      /// skeleton of their own.</summary>
      public Boolean JointsAreNodes { get; set; }

      /// <summary>The ID of the node the source file names the skeleton's root; null when it names none.</summary>
      public String SkeletonRoot { get; set; }

      /// <summary>What the source file says of the skin beyond the rest.</summary>
      public ExtraData Extra { get; set; }
   }
}