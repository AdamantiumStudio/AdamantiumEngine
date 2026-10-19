using System;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>How a texture is read between and beyond its pixels. A filter left null is the reader's to choose.</summary>
   public class Sampler
   {
      public String ID { get; set; }

      public String Name { get; set; }

      public TextureFilter? MagFilter { get; set; }

      public TextureFilter? MinFilter { get; set; }

      /// <summary>How mip levels are blended; null when the file's minification uses none.</summary>
      public TextureFilter? MipFilter { get; set; }

      public TextureWrap WrapS { get; set; }

      public TextureWrap WrapT { get; set; }

      public ExtraData Extra { get; set; }
   }
}
