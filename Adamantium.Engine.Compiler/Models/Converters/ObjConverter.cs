using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Containers;
using Adamantium.Engine.Compiler.Converter.Parsers;
using Adamantium.Engine.Compiler.Models.ConversionUtils;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Compiler.Converter.Converters
{
   public class ObjConverter: ConverterBase
   {
      private ObjDataContainer dataContainer;
      private ObjConversionExecutor executor;
      public ObjConverter(string filePath, ConversionConfig config) :base(filePath, config)
      {
         Parser = new ObjFileParser(filePath);
         executor = new ObjConversionExecutor(Config, UpAxis.Y_UP_RH);
      }

      protected override void Convert()
      {
         dataContainer = (ObjDataContainer) Parser.ParseData(Config);
         if (!dataContainer.IsFileValid)
         {
            IsCanceled = true;
            return;
         }

         SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(dataContainer.FileName));
         // Every mesh's faces first: smoothing groups span the whole file, so the normals are worked out over all of them.
         var meshes = new List<ObjMeshData>(dataContainer.Meshes.Values);
         var containersOfMeshes = new List<List<IndicesContainer>>();
         foreach (var meshData in meshes)
         {
            var containers = new List<IndicesContainer>();
            foreach (var indices in meshData.GeometrySemantic)
            {
               containers.Add(executor.DistributeIndices(indices));
            }

            containersOfMeshes.Add(containers);
         }

         var allContainers = new List<IndicesContainer>();
         foreach (var containers in containersOfMeshes)
         {
            allContainers.AddRange(containers);
         }

         executor.SmoothGroups(allContainers, dataContainer.GeometryData);
         for (int i = 0; i < meshes.Count; i++)
         {
            executor.ConstructMeshGeometry(SceneDataContainer, containersOfMeshes[i], dataContainer.GeometryData, meshes[i]);
         }

         foreach (var material in dataContainer.Materials)
         {
            ParseData(material.Value);
         }
      }

      private void ParseData(ObjMaterialData materialData)
      {
         SceneData.Material material = executor.GetMaterial(materialData);
         lock (SceneDataContainer.Materials)
         {
            SceneDataContainer.Materials.Add(material.ID, material);
         }

         GetImages(material);
      }

      private void GetImages(SceneData.Material material)
      {
         var directory = Path.GetDirectoryName(dataContainer.FilePath);
         lock (SceneDataContainer.Images)
         {
            if (!String.IsNullOrEmpty(material.AmbientMap) &&
                !SceneDataContainer.Images.ContainsKey(material.AmbientMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.AmbientMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.AlphaTextureMap) &&
                !SceneDataContainer.Images.ContainsKey(material.AlphaTextureMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.AlphaTextureMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.DiffuseMap) &&
                !SceneDataContainer.Images.ContainsKey(material.DiffuseMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.DiffuseMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.SpecularColorMap) &&
                !SceneDataContainer.Images.ContainsKey(material.SpecularColorMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.SpecularColorMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.SpecularHighlightMap) &&
                !SceneDataContainer.Images.ContainsKey(material.SpecularHighlightMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.SpecularHighlightMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.DisplacementMap) &&
                !SceneDataContainer.Images.ContainsKey(material.DisplacementMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.DisplacementMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
            if (!String.IsNullOrEmpty(material.BumpMap) &&
                !SceneDataContainer.Images.ContainsKey(material.BumpMap))
            {
               var img = new SceneData.Image();
               img.ImageName = material.BumpMap;
               img.FilePath = Path.Combine(directory, img.ImageName);
               SceneDataContainer.Images.Add(img.ImageName, img);
            }
         }
      }
   }
}
