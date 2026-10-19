using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>A texture: the image it shows and the sampler it is read with, by their keys in
   /// <see cref="Images"/> and <see cref="Samplers"/>.</summary>
   public class Texture
   {
      public String ID { get; set; }

      public String Name { get; set; }

      /// <summary>The image, null when the texture has only <see cref="Sources"/>.</summary>
      public String Image { get; set; }

      public String Sampler { get; set; }

      /// <summary>Images in other formats, by the extension giving each - KHR_texture_basisu (KTX2), EXT_texture_webp,
      /// EXT_texture_avif.</summary>
      public Dictionary<String, String> Sources { get; set; } = new();

      public ExtraData Extra { get; set; }
   }
}
