using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>A scene of the source file: its name and the IDs of its root nodes, in <see cref="Models"/> or in
   /// <see cref="OtherNodes"/>.</summary>
   public class Scene
   {
      public String Name { get; set; }

      public List<String> Roots { get; set; } = [];

      public ExtraData Extra { get; set; }
   }
}
