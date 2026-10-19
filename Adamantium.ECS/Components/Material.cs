using System;
using System.ComponentModel;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using BindableAttribute = Adamantium.MVVM.BindableAttribute;
using Component = Adamantium.ECS.Components.Component;
using TextureDimension = Adamantium.Graphics.Core.Models.TextureDimension;
using AlphaMode = Adamantium.Graphics.Core.Models.AlphaMode;

namespace Adamantium.ECS.Components
{
   [ViewModel]
   public partial class Material : Component
   {
      /// <summary>A plain light grey, opaque: the grey a material with no color of its own is drawn in.</summary>
      public static readonly Vector4F DefaultDiffuseColor = new(0.75f, 0.75f, 0.75f, 1);

      public Material()
      {
         BoundingBoxColor = Colors.White.ToVector4();
         DiffuseColor = DefaultDiffuseColor;
         RoughnessFactor = 1;
         NormalScale = 1;
         OcclusionStrength = 1;
         AlphaCutoff = 0.5f;
      }

      [Bindable]
      public partial Texture Texture { get; set; }

      [Bindable]
      public partial TextureDimension TextureDimension { get; set; }

      [Bindable]
      public partial String TexturePath { get; set; }

      //public SamplerState Sampler { get; set; }

      [Bindable]
      public partial Vector4F Emission { get; set; }

      [Bindable]
      public partial Vector4F DiffuseColor { get; set; }

      [Bindable]
      public partial Vector4F AmbientColor { get; set; }

      [Bindable]
      public partial Vector4F SpecularColor { get; set; }

      [DefaultValue(1.0f)]
      [Bindable]
      public partial Single Shininess { get; set; }

      [Bindable]
      public partial Vector4F Reflective { get; set; }

      [Bindable]
      public partial Vector4F Transparent { get; set; }

      [Bindable]
      public partial Single Reflectivity { get; set; }

      [DefaultValue(1.0f)]
      [Bindable]
      public partial Single Transparency { get; set; }

      [Bindable]
      public partial Single RefractionIndex { get; set; }

      [Bindable]
      public partial Vector4F HighlightColor { get; set; }

      [Bindable]
      public partial Vector3F MeshColor { get; set; }

      [Bindable]
      public partial Vector4F BoundingBoxColor { get; set; }

      [Bindable]
      public partial Vector4F LightColor { get; set; }

      /// <summary>How metallic the surface is, 0 to 1. Kept for PBR; the forward renderer draws the base color.</summary>
      [Bindable]
      public partial Single MetallicFactor { get; set; }

      /// <summary>How rough the surface is, 0 to 1. Kept for PBR.</summary>
      [Bindable]
      public partial Single RoughnessFactor { get; set; }

      /// <summary>Where the image of roughness (green) and metalness (blue) came from.</summary>
      [Bindable]
      public partial String MetallicRoughnessPath { get; set; }

      /// <summary>Where the normal map came from.</summary>
      [Bindable]
      public partial String NormalMapPath { get; set; }

      /// <summary>How strongly the normal map bends the normal.</summary>
      [Bindable]
      public partial Single NormalScale { get; set; }

      /// <summary>Where the ambient occlusion map came from.</summary>
      [Bindable]
      public partial String OcclusionMapPath { get; set; }

      /// <summary>How much of the occlusion map is applied, 0 to 1.</summary>
      [Bindable]
      public partial Single OcclusionStrength { get; set; }

      /// <summary>Where the image of the light the surface gives off came from.</summary>
      [Bindable]
      public partial String EmissiveMapPath { get; set; }

      /// <summary>How the alpha of the base color is taken.</summary>
      [Bindable]
      public partial AlphaMode AlphaMode { get; set; }

      /// <summary>Below this alpha a masked surface is left out.</summary>
      [Bindable]
      public partial Single AlphaCutoff { get; set; }

      /// <summary>Whether both sides of the surface are drawn and lit.</summary>
      [Bindable]
      public partial Boolean DoubleSided { get; set; }
   }
}
