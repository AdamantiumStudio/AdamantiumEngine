using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class TextureCollection : Dictionary<String, SceneData.Texture>
   {
      public TextureCollection()
      {
      }

      public TextureCollection(TextureCollection textures) : base(textures)
      {
      }
   }
}
