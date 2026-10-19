using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class Light
   {
      public Light()
      {
         Scale = Vector3F.One;
         Rotation = QuaternionF.Identity;
      }

      public Light(Light copy)
      {
         ID = copy.ID;
         Name = copy.Name;
         Position = copy.Position;
         Color = new Vector3F(copy.Color.ToArray());
         LightType = copy.LightType;
      }

      public String Name { get; set; }

      public String ID { get; set; }

      public LightType LightType { get; set; }

      public Vector3F Color { get; set; }

      public Vector3F Position { get; set; }

      public Vector3F Scale { get; set; }

      public QuaternionF Rotation { get; set; }

      //Константное затухание
      public Single ConstantAttenuation { get; set; }

      //Линейное затухание
      public Single LinearAttenuation { get; set; }

      //Квадратичное затухание
      public Single QuadraticAttenuation { get; set; }

      public Single FalloffAngle { get; set; }

      public Single FalloffExponent { get; set; }

      /// <summary>How bright the light is, apart from its <see cref="Color"/>: candela for a point or spot light, lux
      /// for a directional one, as glTF has it; 1 for a light that gives none.</summary>
      public Single Intensity { get; set; } = 1;

      /// <summary>How far the light reaches; 0 for no end.</summary>
      public Single Range { get; set; }

      /// <summary>A spot light's full-strength cone, in degrees; its <see cref="FalloffAngle"/> is where it ends.</summary>
      public Single InnerConeAngle { get; set; }

      /// <summary>What the source file says of the light beyond the rest.</summary>
      public ExtraData Extra { get; set; }
   }
}