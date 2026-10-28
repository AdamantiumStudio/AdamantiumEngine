using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class KeyFrame
   {
      public KeyFrame()
      {
      }

      public KeyFrame(Vector3F scale, Vector3F position, QuaternionF rotation,
         InterpolationType interpolation = InterpolationType.Linear)
      {
         Scale = scale;
         Position = position;
         Rotation = rotation;
         Interpolation = interpolation;
      }

      public Double TimeStamp { get; set; }
      public Vector3F Scale { get; set; }
      public Vector3F Position { get; set; }
      public QuaternionF Rotation { get; set; }
      public InterpolationType Interpolation { get; set; }

      public Matrix4x4F ComposeMatrix()
      {
         return Matrix4x4.CreateScale<float>(Scale)*Matrix4x4.CreateFromQuaternion<float>(Rotation)*Matrix4x4.CreateTranslation<float>(Position);
      }
   }
}