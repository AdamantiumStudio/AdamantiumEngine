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

      public String Name { get; set; }

      /// <summary>The base color texture as the file refers to it; its transform is already laid on the texture
      /// coordinates it reads.</summary>
      public TextureReference BaseColorTexture { get; set; }

      public TextureReference MetallicRoughnessTexture { get; set; }

      public TextureReference NormalTexture { get; set; }

      public TextureReference OcclusionTexture { get; set; }

      public TextureReference EmissiveTexture { get; set; }

      /// <summary>The emission's color before <see cref="EmissiveStrength"/>; <see cref="Emission"/> has both.</summary>
      public Vector3F EmissiveFactor { get; set; }

      /// <summary>KHR_materials_emissive_strength: how many times brighter than its color the emission is.</summary>
      public Single EmissiveStrength { get; set; } = 1;

      /// <summary>KHR_materials_ior: the index of refraction; 1.5 when the file gives none.</summary>
      public Single Ior { get; set; } = 1.5f;

      /// <summary>KHR_materials_dispersion: how far colors part on refraction, in 20/Abbe number; 0 for none.</summary>
      public Single Dispersion { get; set; }

      /// <summary>KHR_materials_unlit: shown as its color, with no light on it.</summary>
      public Boolean Unlit { get; set; }

      public Clearcoat Clearcoat { get; set; }

      public Sheen Sheen { get; set; }

      public Transmission Transmission { get; set; }

      public DiffuseTransmission DiffuseTransmission { get; set; }

      public Volume Volume { get; set; }

      public Specular Specular { get; set; }

      public Iridescence Iridescence { get; set; }

      public Anisotropy Anisotropy { get; set; }

      public SpecularGlossiness SpecularGlossiness { get; set; }

      /// <summary>What the source file says of the material beyond the rest.</summary>
      public ExtraData Extra { get; set; }

      /// <summary>What the source file says of the metallic-roughness part of the material beyond its values.</summary>
      public ExtraData PbrExtra { get; set; }
   }
}