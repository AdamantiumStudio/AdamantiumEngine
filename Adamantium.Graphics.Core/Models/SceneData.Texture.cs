using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public enum TextureFilter
   {
      Nearest,
      Linear
   }

   public enum TextureWrap
   {
      Repeat,
      ClampToEdge,
      MirroredRepeat
   }

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

   /// <summary>A material's use of a texture: which one, on which texture coordinates, moved how.</summary>
   public class TextureReference
   {
      /// <summary>The texture's key in <see cref="Textures"/>.</summary>
      public String Texture { get; set; }

      /// <summary>The set of texture coordinates it is read with.</summary>
      public Int32 TexCoord { get; set; }

      /// <summary>KHR_texture_transform as the file has it; null when there is none.</summary>
      public TextureTransform Transform { get; set; }

      public ExtraData Extra { get; set; }
   }

   /// <summary>How texture coordinates are moved before a texture is read: scaled, then turned by
   /// <see cref="Rotation"/> radians, then offset; <see cref="TexCoord"/> over the reference's when set.</summary>
   public class TextureTransform
   {
      public Vector2F Offset { get; set; }

      public Single Rotation { get; set; }

      public Vector2F Scale { get; set; } = Vector2F.One;

      public Int32? TexCoord { get; set; }
   }
}
