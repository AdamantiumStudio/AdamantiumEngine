using System;
using System.IO;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Converters;
using Adamantium.Engine.Compiler.Models.Converters;

namespace Adamantium.Engine.Compiler.Converter
{
   public static class ModelConverterFactory
   {
      public static ConverterBase GetConverter(string filePath, ConversionConfig config)
      {
         String extention = Path.GetExtension(filePath).ToLower();
         switch (extention)
         {
            case ".dae":
               return new ColladaConverter(filePath, config);
            case ".obj":
               return new ObjConverter(filePath, config);
            case ".3ds":
               return new Autodesk3DSConverter(filePath, config);
            case ".gltf":
            case ".glb":
               return new GltfConverter(filePath, config);
            case ".u3d":
               return new U3dConverter(filePath, config);
            case ".prc":
               return new PrcConverter(filePath, config);
            default:
               throw new NotSupportedException("This converter type is not supported");
         }

      }

      /// <summary>The converter of a model held in memory - one carried inside another file, a PDF's 3D artwork say -
      /// by the extension of <paramref name="fileName"/>, the name it goes by.</summary>
      public static ConverterBase GetConverter(string fileName, byte[] bytes, ConversionConfig config)
      {
         ArgumentNullException.ThrowIfNull(bytes);
         var extension = Path.GetExtension(fileName).ToLowerInvariant();
         return extension switch
         {
            ".u3d" => new U3dConverter(fileName, bytes, config),
            ".prc" => new PrcConverter(fileName, bytes, config),
            _ => throw new NotSupportedException($"A {extension} model is not read from memory")
         };
      }
   }
}
