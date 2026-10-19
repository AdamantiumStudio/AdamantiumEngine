using System;
using System.ComponentModel;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class Material
   {
      public Material()
      {
      }

      public String ID { get; set; }

      public String MeshID { get; set; }

      public Vector4F Emission { get; set; }

      public Vector4F DiffuseColor { get; set; }

      public Vector4F AmbientColor { get; set; }

      public Vector4F SpecularColor { get; set; }

      [DefaultValue(1.0f)]
      public Single Shininess { get; set; }

      public Vector4F Reflective { get; set; }

      public Vector4F Transparent { get; set; }

      public Single Reflectivity { get; set; }

      public Single Transparency { get; set; }

      public Single RefractionIndex { get; set; }


      public String AmbientMap { get; set; }

      public String DiffuseMap { get; set; }

      public String SpecularColorMap { get; set; }

      public String SpecularHighlightMap { get; set; }

      public String AlphaTextureMap { get; set; }

      public String DisplacementMap { get; set; }

      public String BumpMap { get; set; }

      public String ReflectionMap { get; set; }

      /// <summary>How metallic the surface is, 0 to 1, times the blue of <see cref="MetallicRoughnessMap"/>.</summary>
      public Single MetallicFactor { get; set; }

      /// <summary>How rough the surface is, 0 to 1, times the green of <see cref="MetallicRoughnessMap"/>.</summary>
      public Single RoughnessFactor { get; set; } = 1;

      /// <summary>The image of roughness (green) and metalness (blue), as glTF packs them.</summary>
      public String MetallicRoughnessMap { get; set; }

      /// <summary>The tangent-space normal map.</summary>
      public String NormalMap { get; set; }

      /// <summary>How strongly <see cref="NormalMap"/> bends the normal.</summary>
      public Single NormalScale { get; set; } = 1;

      /// <summary>The ambient occlusion map, in its red.</summary>
      public String OcclusionMap { get; set; }

      /// <summary>How much of <see cref="OcclusionMap"/> is applied, 0 to 1.</summary>
      public Single OcclusionStrength { get; set; } = 1;

      /// <summary>The image of the light the surface gives off, times <see cref="Emission"/>.</summary>
      public String EmissiveMap { get; set; }

      /// <summary>How the alpha of the base color is taken.</summary>
      public AlphaMode AlphaMode { get; set; }

      /// <summary>Below this alpha a <see cref="AlphaMode.Mask"/> surface is not drawn.</summary>
      public Single AlphaCutoff { get; set; } = 0.5f;

      /// <summary>Whether both sides of the surface are drawn and lit.</summary>
      public Boolean DoubleSided { get; set; }
   }
}