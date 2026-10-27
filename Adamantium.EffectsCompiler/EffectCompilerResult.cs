using System.Collections.Generic;
using Adamantium.Core;

namespace Adamantium.EffectsCompiler
{
   public sealed class EffectCompilerResult
   {
      /// <summary>
      /// Initializes a new instance of the <see cref="EffectCompilerResult" /> class.
      /// </summary>
      /// <param name="effectData">The EffectData.</param>
      /// <param name="logger">The logger.</param>
      /// <param name="includes">The headers the compile pulled in.</param>
      public EffectCompilerResult(EffectData effectData, Logger logger, IReadOnlyList<string> includes)
      {
         EffectData = effectData;
         Logger = logger;
         Includes = includes;
      }

      /// <summary>
      /// Every header the compile pulled in, nested ones included, as the include set names them (a path when it has
      /// one, else a file name): what to watch so a change to any of them recompiles this effect.
      /// </summary>
      public IReadOnlyList<string> Includes { get; }

      /// <summary>
      /// Gets the EffectData.
      /// </summary>
      /// <value>The EffectData.</value>
      public EffectData EffectData { get; private set; }

      /// <summary>
      /// Gets a value indicating whether this instance has errors.
      /// </summary>
      /// <value><c>true</c> if this instance has errors; otherwise, <c>false</c>.</value>
      public bool HasErrors => Logger.HasErrors;

      /// <summary>
      /// Gets the logger containing compilation messages..
      /// </summary>
      /// <value>The logger.</value>
      public Logger Logger { get; private set; }
   }
}
