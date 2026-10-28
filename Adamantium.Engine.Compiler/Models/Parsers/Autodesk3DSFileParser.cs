using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Containers;
using Adamantium.Engine.Compiler.Models.ConversionUtils;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Converter.Parsers
{
   /*
     0x4D4D // Main Chunk
      ├─ 0x0002 // M3D Version
      ├─ 0x3D3D // 3D Editor Chunk
      │  ├─ 0x4000 // Object Block
      │  │  ├─ 0x4100 // Triangular Mesh
      │  │  │  ├─ 0x4110 // Vertices List
      │  │  │  ├─ 0x4120 // Faces Description
      │  │  │  │  ├─ 0x4130 // Faces Material
      │  │  │  │  └─ 0x4150 // Smoothing Group List
      │  │  │  ├─ 0x4140 // Mapping Coordinates List
      │  │  │  └─ 0x4160 // Local Coordinates System
      │  │  ├─ 0x4600 // Light
      │  │  │  └─ 0x4610 // Spotlight
      │  │  └─ 0x4700 // Camera
      │  └─ 0xAFFF // Material Block
      │     ├─ 0xA000 // Material Name
      │     ├─ 0xA010 // Ambient Color
      │     ├─ 0xA020 // Diffuse Color
      │     ├─ 0xA030 // Specular Color
      │     ├─ 0xA200 // Texture Map 1
      │     ├─ 0xA230 // Bump Map
      │     └─ 0xA220 // Reflection Map
      │        │  // Sub Chunks For Each Map //
      │        ├─ 0xA300 // Mapping Filename
      │        └─ 0xA351 // Mapping Parameters
      └─ 0xB000 // Keyframer Chunk
         ├─ 0xB002 // Mesh Information Block
         ├─ 0xB007 // Spot Light Information Block
         └─ 0xB008 // Frames (Start and End)
            ├─ 0xB010 // Object Name
            ├─ 0xB013 // Object Pivot Point
            ├─ 0xB020 // position Track
            ├─ 0xB021 // Rotation Track
            ├─ 0xB022 // Scale Track
            └─ 0xB030 // Hierarchy position
   */

   public class Autodesk3DSFileParser : ModelFileParser
   {
      public Autodesk3DSFileParser(string filePath) : base(filePath)
      {

      }

      private Autodesk3DsDataContainer dataContainer;
      private FileStream fileStream;
      private BinaryReader binaryReader;

      public override DataContainer ParseData(ConversionConfig config)
      {
         dataContainer = new Autodesk3DsDataContainer(FilePath);
         if (!File.Exists(FilePath))
         {
            dataContainer.IsFileValid = false;
            return dataContainer;
         }
         dataContainer.Metadata = new FileMetadata();
         fileStream = new FileStream(FilePath, FileMode.Open);
         binaryReader = new BinaryReader(fileStream);
         bytesCount = (int)fileStream.Length;
         var root =  dataContainer.Data.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FilePath));
         dataContainer.Data.Models = root;
         fileStream.Position = 0;
         ReadData(root);
         //The last mesh has no next ObjectDefinition to close it
         FinalizeGeometry(ref currentGeometry);

         binaryReader.Close();
         fileStream.Close();

         return dataContainer;

      }

      private int bytesCount;

      //The walk descends into itself, so the mesh being read cannot live in a local
      private Mesh currentGeometry;

      //Raw mesh data, kept until the whole mesh has been read - see the VertexList case
      private List<Vector3<double>> rawPositions;
      private List<Vector2F> rawUVs;
      private List<int> faceIndices;
      private List<uint> smoothingGroups;

      /// <summary>Puts one mesh together once every chunk of it has been read. 3DS stores no vertex normals, only a
      /// smoothing group mask per face: two faces sharing a vertex blend their normals when their masks overlap and
      /// keep their own when they do not - which is exactly what a hard edge is.</summary>
      private void FinalizeGeometry(ref Mesh geometry)
      {
         if (geometry == null || rawPositions == null) return;

         if (faceIndices == null || faceIndices.Count == 0)
         {
            geometry.SetPoints(rawPositions);
            geometry = null;
            return;
         }

         var faceNormals = new Vector3F[faceIndices.Count / 3];
         for (var face = 0; face < faceNormals.Length; face++)
         {
            var a = rawPositions[faceIndices[face * 3]].As<float>();
            var b = rawPositions[faceIndices[face * 3 + 1]].As<float>();
            var c = rawPositions[faceIndices[face * 3 + 2]].As<float>();
            faceNormals[face] = Vector3F.Cross(b - a, c - a);
         }

         // Which faces touch each vertex. Built once: comparing every face against every other one is quadratic,
         // and a single mesh here can carry thousands of them.
         var incident = new Dictionary<int, List<int>>();
         for (var face = 0; face < faceNormals.Length; face++)
         {
            for (var corner = 0; corner < 3; corner++)
            {
               var vertex = faceIndices[face * 3 + corner];
               if (!incident.TryGetValue(vertex, out var faces)) incident[vertex] = faces = new List<int>();
               faces.Add(face);
            }
         }

         // Per vertex and group, the sum of the normals of every incident face sharing a bit of that group. A face
         // with no group blends with nobody, which is what the format means by flat.
         var blended = new Dictionary<(int Vertex, uint Group), Vector3F>();
         for (var face = 0; face < faceNormals.Length; face++)
         {
            var group = Group(face);
            if (group == 0) continue;

            for (var corner = 0; corner < 3; corner++)
            {
               var vertex = faceIndices[face * 3 + corner];
               if (blended.ContainsKey((vertex, group))) continue;

               var sum = Vector3F.Zero;
               foreach (var otherFace in incident[vertex])
               {
                  if ((group & Group(otherFace)) != 0) sum += faceNormals[otherFace];
               }
               blended[(vertex, group)] = sum;
            }
         }

         uint Group(int face) => face < smoothingGroups.Count ? smoothingGroups[face] : 0u;

         var positions = new List<Vector3<double>>(faceIndices.Count);
         var normals = new List<Vector3F>(faceIndices.Count);
         var uvs = new List<Vector2F>(faceIndices.Count);
         var hasUVs = rawUVs.Count == rawPositions.Count;

         for (var face = 0; face < faceNormals.Length; face++)
         {
            var group = Group(face);
            for (var corner = 0; corner < 3; corner++)
            {
               var vertex = faceIndices[face * 3 + corner];
               positions.Add(rawPositions[vertex]);
               if (hasUVs) uvs.Add(rawUVs[vertex]);

               var normal = group != 0 && blended.TryGetValue((vertex, group), out var sum)
                  ? sum
                  : faceNormals[face];
               normals.Add(Vector3F.Normalize(normal));
            }
         }

         geometry.SetPoints(positions);
         geometry.SetNormals(normals);
         if (hasUVs) geometry.SetUVs(0, uvs);
         geometry.GenerateBasicIndices();

         geometry = null;
         rawPositions = null;
         rawUVs = null;
         faceIndices = null;
         smoothingGroups = null;
      }

      private void ReadData(SceneData.Model rootMesh)
      {
         SceneData.Model currentMesh = null;
         SceneData.Material material = null;
         MaterialTextureType textureType = MaterialTextureType.None;
         ColorType colorType = ColorType.None;
         while (fileStream.Position < bytesCount)
         {
            Autodesk3DSChunks chunk = (Autodesk3DSChunks)binaryReader.ReadUInt16();
            int length = binaryReader.ReadInt32();
            ushort count;
            switch (chunk)
            {
               case Autodesk3DSChunks.Main3DS:
                  break;

               case Autodesk3DSChunks.EditorConfig3DS:
                  break;

               case Autodesk3DSChunks.Version:
                  var version = binaryReader.ReadUInt32();
                  dataContainer.Metadata.Version = version.ToString();
                  break;

               case Autodesk3DSChunks.OneUnit:
                  var units = binaryReader.ReadSingle();
                  dataContainer.Data.Units = new SceneData.Unit(UnitType.Meter, units);
                  break;

               case Autodesk3DSChunks.ObjectDefinition:
                  FinalizeGeometry(ref currentGeometry);
                  var meshName = ReadName();
                  currentMesh = dataContainer.Data.CreateMesh(rootMesh, "", meshName);
                  break;

               case Autodesk3DSChunks.Mesh:
                  if (length == 6)
                  {
                     ReadData(currentMesh);
                  }
                  break;

               case Autodesk3DSChunks.VertexList:
                  count = binaryReader.ReadUInt16();
                  var vertexCount = Convert.ToInt32(count);
                  if (vertexCount > 0)
                  {
                     FinalizeGeometry(ref currentGeometry);
                     currentGeometry = new Mesh { MeshTopology = PrimitiveType.TriangleList };
                     // Assembling right here was the trouble: the UV chunk comes AFTER the faces, so by the time
                     // the coordinates arrived the points had already been reordered and no longer matched them.
                     // Everything is kept raw now and put together once the whole mesh has been read.
                     rawPositions = new List<Vector3<double>>(vertexCount);
                     rawUVs = new List<Vector2F>();
                     faceIndices = new List<int>();
                     smoothingGroups = new List<uint>();
                     for (int x = 0; x < vertexCount; x++)
                     {
                        Vector3<double> vertex;
                        vertex.X = binaryReader.ReadSingle();
                        vertex.Y = binaryReader.ReadSingle();
                        vertex.Z = binaryReader.ReadSingle();
                        rawPositions.Add(vertex);
                     }
                     currentMesh.Meshes.Add(currentGeometry);
                  }
                  else
                  {
                     ReadData(currentMesh);
                  }
                  break;

               case Autodesk3DSChunks.FaceDescription:
                  count = binaryReader.ReadUInt16();
                  var polygonCount = Convert.ToInt32(count);
                  for (int j = 0; j < polygonCount; j++)
                  {
                     faceIndices.Add(binaryReader.ReadUInt16());
                     faceIndices.Add(binaryReader.ReadUInt16());
                     faceIndices.Add(binaryReader.ReadUInt16());
                     binaryReader.ReadUInt16();
                  }
                  break;

               // One group mask per face. This is where 3DS keeps its hard edges: the format has no vertex normals
               // at all, so without this chunk every model imports uniformly smooth and nobody says a word.
               case Autodesk3DSChunks.FaceSmoothingGroup:
                  var groupCount = faceIndices == null ? 0 : faceIndices.Count / 3;
                  for (int j = 0; j < groupCount; j++)
                  {
                     smoothingGroups.Add(binaryReader.ReadUInt32());
                  }
                  break;

               case Autodesk3DSChunks.FaceMaterial:
                  currentGeometry.MaterialID = ReadName();
                  
                  count = binaryReader.ReadUInt16();
                  for (ushort y = 0; y < count; y++)
                  {
                     int index = binaryReader.ReadUInt16();
                  }
                  break;

               case Autodesk3DSChunks.UVCoordinates:
                  count = binaryReader.ReadUInt16();
                  for (ushort y = 0; y < count; y++)
                  {
                     // The pair was read into a local and never added to the list, so SetUVs got an empty one:
                     // every 3DS model arrived without texture coordinates at all.
                     Vector2F uv;
                     uv.X = binaryReader.ReadSingle();
                     uv.Y = binaryReader.ReadSingle();
                     rawUVs?.Add(uv);
                  }
                  break;

               case Autodesk3DSChunks.WorldMatrix:
                  Matrix4x4F matrix = Matrix4x4F.Identity;

                  matrix.M11 = binaryReader.ReadSingle();
                  matrix.M12 = binaryReader.ReadSingle();
                  matrix.M13 = binaryReader.ReadSingle();

                  matrix.M21 = binaryReader.ReadSingle();
                  matrix.M22 = binaryReader.ReadSingle();
                  matrix.M23 = binaryReader.ReadSingle();

                  matrix.M31 = binaryReader.ReadSingle();
                  matrix.M32 = binaryReader.ReadSingle();
                  matrix.M33 = binaryReader.ReadSingle();

                  matrix.M41 = binaryReader.ReadSingle();
                  matrix.M42 = binaryReader.ReadSingle();
                  matrix.M43 = binaryReader.ReadSingle();

                  if (currentMesh != null)
                  {
                     Vector3F scale, translation;
                     QuaternionF rotation;
                     matrix.Decompose(out scale, out rotation, out translation);
                     currentMesh.Scale = scale;
                     currentMesh.Rotation = rotation;
                     currentMesh.Position = translation;
                  }
                  break;

               case Autodesk3DSChunks.MaterialBlock:
                  break;

               case Autodesk3DSChunks.MaterialName:
                  var name = ReadName();
                  material = new SceneData.Material();
                  material.ID = name;

                  if (!dataContainer.Data.Materials.ContainsKey(name))
                  {
                     dataContainer.Data.Materials.Add(material.ID, material);
                  }
                  break;

               case Autodesk3DSChunks.AmbientColor:
                  colorType = ColorType.Ambient;
                     break;

               case Autodesk3DSChunks.DiffuseColor:
                  colorType = ColorType.Diffuse;
                  break;

               case Autodesk3DSChunks.SpecularColor:
                  colorType = ColorType.Specular;
                  break;

               case Autodesk3DSChunks.RgbColorByte:
                  Vector4F color = new Vector4F();
                  color.X = binaryReader.ReadByte();
                  color.Y = binaryReader.ReadByte();
                  color.Z = binaryReader.ReadByte();
                  color.W = 1;
                  switch (colorType)
                  {
                     case ColorType.Ambient:
                        material.AmbientColor = color;
                        break;
                     case ColorType.Diffuse:
                        material.DiffuseColor = color;
                        break;
                     case ColorType.Specular:
                        material.SpecularColor = color;
                        break;
                  }
                  break;

               case Autodesk3DSChunks.TextureMap1:
                  textureType = MaterialTextureType.TextureMap;
                  break;

               case Autodesk3DSChunks.ReflectionMap:
                  textureType = MaterialTextureType.ReflectionMap;
                  break;

               case Autodesk3DSChunks.BumpMap:
                  textureType = MaterialTextureType.BumpMap;
                  break;

               case Autodesk3DSChunks.MappingFileName:
                  var filePath = ReadName();
                  if (material == null)
                  {
                     material = new SceneData.Material();
                  }
                  switch (textureType)
                  {
                     case MaterialTextureType.TextureMap:
                        material.DiffuseMap = filePath;
                        break;
                     case MaterialTextureType.BumpMap:
                        material.BumpMap = filePath;
                        break;
                     case MaterialTextureType.ReflectionMap:
                        material.ReflectionMap = filePath;
                        break;
                  }
                  SceneData.Image image = new SceneData.Image();
                  image.FilePath = filePath;
                  image.ImageName = Path.GetFileName(filePath);
                  dataContainer.Data.Images.Add(filePath, image);
                  break;


               default:
                  fileStream.Position += length - 6;
                  break;
            }
         }
      }

      private string ReadName()
      {
         List<char> chars = new List<char>();
         char symbol = '\0';
         do
         {
            symbol = (char)binaryReader.ReadByte();
            if (symbol != '\0')
            {
               chars.Add(symbol);
            }
         } while (symbol != '\0');

         return new string(chars.ToArray());
      }

      private enum MaterialTextureType
      {
         None,
         BumpMap,
         TextureMap,
         ReflectionMap
      }

      private enum ColorType
      {
         None,
         Ambient,
         Diffuse,
         Specular
      }
   }
}
