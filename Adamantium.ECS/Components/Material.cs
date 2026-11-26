using System;
using System.ComponentModel;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using BindableAttribute = Adamantium.MVVM.BindableAttribute;
using Component = Adamantium.ECS.Components.Component;
using TextureDimension = Adamantium.Graphics.Core.Models.TextureDimension;
using AlphaMode = Adamantium.Graphics.Core.Models.AlphaMode;
using MaterialFaces = Adamantium.Graphics.Core.Models.MaterialFaces;

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

      [Bindable, Affects(nameof(Opacity))]
      public partial Vector4F DiffuseColor { get; set; }

      /// <summary>How much of the surface shows, 0 to 1: the alpha of <see cref="DiffuseColor"/>, which counts unless the
      /// material is opaque.</summary>
      [DoNotClone]
      public float Opacity
      {
         get => DiffuseColor.W;
         set
         {
            if (float.IsNaN(value))
               return;

            DiffuseColor = new Vector4F(DiffuseColor.X, DiffuseColor.Y, DiffuseColor.Z, Math.Clamp(value, 0f, 1f));
         }
      }

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

      /// <summary>How metallic the surface is, 0 to 1, times the blue of <see cref="MetallicRoughnessTexture"/>.</summary>
      [Bindable]
      public partial Single MetallicFactor { get; set; }

      /// <summary>How rough the surface is, 0 to 1, times the green of <see cref="MetallicRoughnessTexture"/>.</summary>
      [Bindable]
      public partial Single RoughnessFactor { get; set; }

      /// <summary>How the surface is lit: the simple model, cheap and needing nothing prepared, or the physical one, with
      /// metalness, roughness and the maps.</summary>
      [Bindable]
      public partial LightingModel Lighting { get; set; }

      /// <summary>The image of roughness (green) and metalness (blue); the physical model only.</summary>
      [Bindable]
      public partial Texture MetallicRoughnessTexture { get; set; }

      /// <summary>The normal map, in the surface's tangent space; the physical model only.</summary>
      [Bindable]
      public partial Texture NormalTexture { get; set; }

      /// <summary>The ambient occlusion map, in its red; the physical model only.</summary>
      [Bindable]
      public partial Texture OcclusionTexture { get; set; }

      /// <summary>The image of the light the surface gives off, times <see cref="Emission"/>.</summary>
      [Bindable]
      public partial Texture EmissiveTexture { get; set; }

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
      [Bindable, Affects(nameof(IsMasked), nameof(UsesAlpha))]
      public partial AlphaMode AlphaMode { get; set; }

      /// <summary>Whether <see cref="AlphaCutoff"/> leaves the surface out where its alpha is below it.</summary>
      public bool IsMasked => AlphaMode == AlphaMode.Mask;

      /// <summary>Whether the alpha - <see cref="Opacity"/> - counts: the material is not opaque.</summary>
      public bool UsesAlpha => AlphaMode != AlphaMode.Opaque;

      /// <summary>Below this alpha a masked surface is left out.</summary>
      [Bindable]
      public partial Single AlphaCutoff { get; set; }

      /// <summary>Which faces of the surface are drawn and lit: both by default.</summary>
      [Bindable]
      public partial MaterialFaces Faces { get; set; }
   }
}
