using System;
using System.ComponentModel;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using BindableAttribute = Adamantium.MVVM.BindableAttribute;
using Component = Adamantium.ECS.Components.Component;
using TextureDimension = Adamantium.Graphics.Core.Models.TextureDimension;

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
   }
}
