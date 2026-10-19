using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

public partial class SceneData
{
   public class SamplerCollection : Dictionary<String, SceneData.Sampler>
   {
      public SamplerCollection()
      {
      }

      public SamplerCollection(SamplerCollection samplers) : base(samplers)
      {
      }
   }
}
