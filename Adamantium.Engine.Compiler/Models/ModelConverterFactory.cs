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
            default:
               throw new NotSupportedException("This converter type is not supported");
         }
         
      }
   }
}
