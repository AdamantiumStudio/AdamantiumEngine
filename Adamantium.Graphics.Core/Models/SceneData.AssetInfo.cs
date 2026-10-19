using System;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>What the source file says of itself: glTF's <c>asset</c>.</summary>
   public class AssetInfo
   {
      public String Version { get; set; }

      public String MinVersion { get; set; }

      public String Generator { get; set; }

      public String Copyright { get; set; }

      public ExtraData Extra { get; set; }
   }
}
