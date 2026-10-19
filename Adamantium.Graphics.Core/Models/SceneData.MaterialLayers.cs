using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>A clear lacquer over the material (KHR_materials_clearcoat).</summary>
   public class Clearcoat
   {
      public Single Factor { get; set; }

      public TextureReference Texture { get; set; }

      public Single RoughnessFactor { get; set; }

      public TextureReference RoughnessTexture { get; set; }

      public TextureReference NormalTexture { get; set; }

      public Single NormalScale { get; set; } = 1;
   }

   /// <summary>Velvet-like back-scatter at grazing angles (KHR_materials_sheen).</summary>
   public class Sheen
   {
      public Vector3F ColorFactor { get; set; }

      public TextureReference ColorTexture { get; set; }

      public Single RoughnessFactor { get; set; }

      public TextureReference RoughnessTexture { get; set; }
   }

   /// <summary>Light passing through a thin surface, as glass (KHR_materials_transmission).</summary>
   public class Transmission
   {
      public Single Factor { get; set; }

      public TextureReference Texture { get; set; }
   }

   /// <summary>Light passing through a thin surface, scattered as through leaves or paper
   /// (KHR_materials_diffuse_transmission).</summary>
   public class DiffuseTransmission
   {
      public Single Factor { get; set; }

      public TextureReference Texture { get; set; }

      public Vector3F ColorFactor { get; set; } = Vector3F.One;

      public TextureReference ColorTexture { get; set; }
   }

   /// <summary>The body under a transmissive surface (KHR_materials_volume).</summary>
   public class Volume
   {
      public Single ThicknessFactor { get; set; }

      public TextureReference ThicknessTexture { get; set; }

      /// <summary>How far light goes before it takes <see cref="AttenuationColor"/>; infinity for never.</summary>
      public Single AttenuationDistance { get; set; } = Single.PositiveInfinity;

      public Vector3F AttenuationColor { get; set; } = Vector3F.One;
   }

   /// <summary>The strength and color of the specular reflection (KHR_materials_specular).</summary>
   public class Specular
   {
      public Single Factor { get; set; } = 1;

      public TextureReference Texture { get; set; }

      public Vector3F ColorFactor { get; set; } = Vector3F.One;

      public TextureReference ColorTexture { get; set; }
   }

   /// <summary>A thin film's rainbow, as of soap or oil (KHR_materials_iridescence). Thicknesses in nanometres.</summary>
   public class Iridescence
   {
      public Single Factor { get; set; }

      public TextureReference Texture { get; set; }

      public Single Ior { get; set; } = 1.3f;

      public Single ThicknessMinimum { get; set; } = 100;

      public Single ThicknessMaximum { get; set; } = 400;

      public TextureReference ThicknessTexture { get; set; }
   }

   /// <summary>Highlights drawn out along a direction, as of brushed metal (KHR_materials_anisotropy).</summary>
   public class Anisotropy
   {
      public Single Strength { get; set; }

      /// <summary>Radians, counter-clockwise from the tangent.</summary>
      public Single Rotation { get; set; }

      public TextureReference Texture { get; set; }
   }

   /// <summary>The older specular-glossiness workflow (KHR_materials_pbrSpecularGlossiness).</summary>
   public class SpecularGlossiness
   {
      public Vector4F DiffuseFactor { get; set; } = Vector4F.One;

      public TextureReference DiffuseTexture { get; set; }

      public Vector3F SpecularFactor { get; set; } = Vector3F.One;

      public Single GlossinessFactor { get; set; } = 1;

      public TextureReference SpecularGlossinessTexture { get; set; }
   }
}
