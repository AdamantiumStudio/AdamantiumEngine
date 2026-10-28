using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>The key frames of one node of a clip - a skeleton's joint or any other.</summary>
   public class FrameCollection:List<KeyFrame>
   {
      /// <summary>The <see cref="Model.ID"/> of the node the frames move.</summary>
      public String JointId { get; set; }

      public FrameCollection()
      {
         JointId = String.Empty;
      }

   }
}
