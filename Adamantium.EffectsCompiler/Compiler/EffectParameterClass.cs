namespace Adamantium.EffectsCompiler
{
   /// <summary>What shape an effect parameter's value has.</summary>
   public enum EffectParameterClass : byte
   {
      /// <summary>One value.</summary>
      Scalar = 0,

      /// <summary>A vector.</summary>
      Vector = 1,

      /// <summary>A matrix stored row by row.</summary>
      MatrixRows = 2,

      /// <summary>A matrix stored column by column.</summary>
      MatrixColumns = 3,

      /// <summary>A resource: a texture, sampler or buffer.</summary>
      Object = 4,

      /// <summary>A structure.</summary>
      Struct = 5,

      /// <summary>An interface class.</summary>
      InterfaceClass = 6,

      /// <summary>An interface pointer.</summary>
      InterfacePointer = 7,
   }
}
