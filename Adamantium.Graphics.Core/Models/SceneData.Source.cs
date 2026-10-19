using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   /// <summary>What the source file says of itself: glTF's <c>asset</c>.</summary>
   public class AssetInfo
   {
      public String Version { get; set; }

      public String MinVersion { get; set; }

      public String Generator { get; set; }

      public String Copyright { get; set; }

      public ExtraData Extra { get; set; }
   }

   /// <summary>Where one copy of a node's meshes stands, relative to the node.</summary>
   public class InstanceTransform
   {
      public Vector3F Position { get; set; }

      public QuaternionF Rotation { get; set; } = QuaternionF.Identity;

      public Vector3F Scale { get; set; } = Vector3F.One;
   }

   /// <summary>A scene of the source file: its name and the IDs of its root nodes, in <see cref="Models"/> or in
   /// <see cref="OtherNodes"/>.</summary>
   public class Scene
   {
      public String Name { get; set; }

      public List<String> Roots { get; set; } = [];

      public ExtraData Extra { get; set; }
   }

   /// <summary>An animation of the source file as it has it, before its channels were made keys of
   /// <see cref="Clips"/>: what each channel moves and its curve, in the file's own axes.</summary>
   public class AnimationSource
   {
      public String Name { get; set; }

      /// <summary>The key of the clip made of it in <see cref="Clips"/>; null when none was.</summary>
      public String Clip { get; set; }

      public List<AnimationChannel> Channels { get; set; } = [];

      public ExtraData Extra { get; set; }
   }

   /// <summary>One channel of an <see cref="AnimationSource"/>: its target, and its curve's times and values as the
   /// file has them - a cubic spline's values carry an in-tangent, a value and an out-tangent for each key.</summary>
   public class AnimationChannel
   {
      /// <summary>The ID of the node it moves; null for a channel aimed by <see cref="Pointer"/> alone.</summary>
      public String Node { get; set; }

      /// <summary>What of the node it moves: translation, rotation, scale, weights - or pointer.</summary>
      public String Path { get; set; }

      /// <summary>The JSON pointer to the property it moves (KHR_animation_pointer); null otherwise.</summary>
      public String Pointer { get; set; }

      /// <summary>LINEAR, STEP or CUBICSPLINE.</summary>
      public String Interpolation { get; set; }

      public Single[] Times { get; set; } = [];

      public Single[] Values { get; set; } = [];

      /// <summary>How many numbers make one value: 3 for a translation, 4 for a rotation, a node's morph target count
      /// for weights.</summary>
      public Int32 Components { get; set; }

      public ExtraData Extra { get; set; }

      public ExtraData TargetExtra { get; set; }

      public ExtraData SamplerExtra { get; set; }
   }
}
