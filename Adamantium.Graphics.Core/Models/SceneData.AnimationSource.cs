using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
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
}
