using System;

namespace Adamantium.EffectsCompiler
{
   /// <summary>How an effect is compiled.</summary>
   [Flags]
   public enum EffectCompilerFlags
   {
      /// <summary>Debug information in the output.</summary>
      Debug = 1,

      /// <summary>No validation of the generated code.</summary>
      SkipValidation = 2,

      /// <summary>No optimization.</summary>
      SkipOptimization = 4,

      /// <summary>Matrices packed row by row.</summary>
      PackMatrixRowMajor = 8,

      /// <summary>Matrices packed column by column.</summary>
      PackMatrixColumnMajor = 16,

      /// <summary>Lower precision where it is faster.</summary>
      PartialPrecision = 32,

      /// <summary>Flow control avoided where it can be.</summary>
      AvoidFlowControl = 512,

      /// <summary>Flow control kept where it can be.</summary>
      PreferFlowControl = 1024,

      /// <summary>Deprecated syntax refused.</summary>
      EnableStrictness = 2048,

      /// <summary>Older shader syntax accepted.</summary>
      EnableBackwardsCompatibility = 4096,

      /// <summary>Floating point as IEEE 754 has it.</summary>
      IeeeStrictness = 8192,

      /// <summary>The lowest optimization.</summary>
      OptimizationLevel0 = 16384,

      /// <summary>The second-lowest optimization, the default.</summary>
      OptimizationLevel1 = 0,

      /// <summary>The second-highest optimization.</summary>
      OptimizationLevel2 = 49152,

      /// <summary>The highest optimization.</summary>
      OptimizationLevel3 = 32768,

      /// <summary>Warnings fail the compilation.</summary>
      WarningsAreErrors = 262144,

      /// <summary>No flags.</summary>
      None = 0,
   }
}
