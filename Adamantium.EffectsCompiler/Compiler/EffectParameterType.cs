namespace Adamantium.EffectsCompiler
{
   /// <summary>The type of an effect parameter's value - a number type, a texture, sampler or buffer kind, or a
   /// pipeline object. The numbers are those the compiled effects store.</summary>
   public enum EffectParameterType : byte
   {
      /// <summary>No type.</summary>
      Void = 0,

      /// <summary>A bool.</summary>
      Bool = 1,

      /// <summary>A 32-bit signed integer.</summary>
      Int = 2,

      /// <summary>A 32-bit float.</summary>
      Float = 3,

      /// <summary>A string.</summary>
      String = 4,

      /// <summary>A texture of any kind.</summary>
      Texture = 5,

      /// <summary>A 1D texture.</summary>
      Texture1D = 6,

      /// <summary>A 2D texture.</summary>
      Texture2D = 7,

      /// <summary>A 3D texture.</summary>
      Texture3D = 8,

      /// <summary>A cube texture.</summary>
      TextureCube = 9,

      /// <summary>A sampler of any kind.</summary>
      Sampler = 10,

      /// <summary>A 1D sampler.</summary>
      Sampler1D = 11,

      /// <summary>A 2D sampler.</summary>
      Sampler2D = 12,

      /// <summary>A 3D sampler.</summary>
      Sampler3D = 13,

      /// <summary>A cube sampler.</summary>
      SamplerCube = 14,

      /// <summary>A pixel shader.</summary>
      Pixelshader = 15,

      /// <summary>A vertex shader.</summary>
      Vertexshader = 16,

      /// <summary>A pixel fragment.</summary>
      Pixelfragment = 17,

      /// <summary>A vertex fragment.</summary>
      Vertexfragment = 18,

      /// <summary>A 32-bit unsigned integer.</summary>
      UInt = 19,

      /// <summary>An 8-bit unsigned integer.</summary>
      UInt8 = 20,

      /// <summary>A geometry shader.</summary>
      Geometryshader = 21,

      /// <summary>Rasterizer state.</summary>
      Rasterizer = 22,

      /// <summary>Depth-stencil state.</summary>
      Depthstencil = 23,

      /// <summary>Blend state.</summary>
      Blend = 24,

      /// <summary>A buffer.</summary>
      Buffer = 25,

      /// <summary>A constant buffer.</summary>
      ConstantBuffer = 26,

      /// <summary>A texture buffer.</summary>
      TextureBuffer = 27,

      /// <summary>An array of 1D textures.</summary>
      Texture1DArray = 28,

      /// <summary>An array of 2D textures.</summary>
      Texture2DArray = 29,

      /// <summary>A render-target view.</summary>
      Rendertargetview = 30,

      /// <summary>A depth-stencil view.</summary>
      Depthstencilview = 31,

      /// <summary>A multisampled 2D texture.</summary>
      Texture2DMultisampled = 32,

      /// <summary>An array of multisampled 2D textures.</summary>
      Texture2DMultisampledArray = 33,

      /// <summary>An array of cube textures.</summary>
      TextureCubeArray = 34,

      /// <summary>A hull shader.</summary>
      Hullshader = 35,

      /// <summary>A domain shader.</summary>
      Domainshader = 36,

      /// <summary>An interface pointer.</summary>
      InterfacePointer = 37,

      /// <summary>A compute shader.</summary>
      Computeshader = 38,

      /// <summary>A 64-bit float.</summary>
      Double = 39,

      /// <summary>A 1D texture read and written.</summary>
      RWTexture1D = 40,

      /// <summary>An array of 1D textures read and written.</summary>
      RWTexture1DArray = 41,

      /// <summary>A 2D texture read and written.</summary>
      RWTexture2D = 42,

      /// <summary>An array of 2D textures read and written.</summary>
      RWTexture2DArray = 43,

      /// <summary>A 3D texture read and written.</summary>
      RWTexture3D = 44,

      /// <summary>A storage image.</summary>
      StorageImage = 45,

      /// <summary>A storage buffer.</summary>
      StorageBuffer = 48,

      /// <summary>A 16-bit signed integer.</summary>
      Int16 = 58,

      /// <summary>A 16-bit unsigned integer.</summary>
      UInt16 = 59,

      /// <summary>A 16-bit float.</summary>
      Float16 = 60,

      /// <summary>A 64-bit signed integer.</summary>
      Int64 = 61,

      /// <summary>A 64-bit unsigned integer.</summary>
      UInt64 = 62,
   }
}
