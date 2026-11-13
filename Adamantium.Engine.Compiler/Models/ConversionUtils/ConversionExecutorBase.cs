using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.ConversionUtils
{
   internal abstract class ConversionExecutorBase
   {
      protected ConversionConfig config;
      protected UpAxis upAxis;

      //AssembleModel changes the axes through AxisConversion - meshes, nodes, bones and key frames at once

      protected ConversionExecutorBase(ConversionConfig config, UpAxis upAxis)
      {
         this.config = config;
         this.upAxis = upAxis;
      }

      internal IndicesContainer DistributeIndices(RawIndicesSemanticData rawIndices)
      {
         var indicesContainer = SplitRawIndices(rawIndices);

         //Triangulate when the faces need it
         if (IsTriangulationRequired(rawIndices.VertexType))
         {
            indicesContainer = Triangulate(rawIndices.Semantic, indicesContainer, rawIndices.VertexType);
         }

         indicesContainer.SmoothingGroups = SmoothingGroupsOfTriangles(rawIndices);
         return indicesContainer;
      }

      // A face of n corners fans into n - 2 triangles, each in the face's group.
      private static List<int> SmoothingGroupsOfTriangles(RawIndicesSemanticData rawIndices)
      {
         var groups = new List<int>();
         if (rawIndices.MeshTopology != PrimitiveType.TriangleList
             || rawIndices.SmoothingGroups.Count != rawIndices.VertexType.Count
             || rawIndices.SmoothingGroups.All(group => group == 0))
         {
            return groups;
         }

         for (int face = 0; face < rawIndices.VertexType.Count; face++)
         {
            for (int triangle = 0; triangle < rawIndices.VertexType[face] - 2; triangle++)
            {
               groups.Add(rawIndices.SmoothingGroups[face]);
            }
         }

         return groups;
      }

      //Splits the interleaved index stream of COLLADA 1.4.0/1.4.1 and .obj into one array per semantic
      internal IndicesContainer SplitRawIndices(RawIndicesSemanticData rawIndices)
      {
         IndicesContainer indicesContainer = new IndicesContainer();
         indicesContainer.MaterialId = rawIndices.MaterialId;
         indicesContainer.Semantic = rawIndices.Semantic;
         indicesContainer.MeshTopology = rawIndices.MeshTopology;

         for (int i = 0; i < rawIndices.RawIndices.Count; i += rawIndices.Offset.CommonOffset + 1)
         {
            for (var j = i; j < i + rawIndices.Offset.CommonOffset + 1; j++)
            {
               if (rawIndices.Offset.Position != null && j - i == (int)rawIndices.Offset.Position)
               {
                  indicesContainer.Positions.Add(rawIndices.RawIndices[j]);
               }

               // Normal indices used to be thrown away, and the mesh then re-derived normals by averaging - losing
               // the very hard edges those indices describe.
               else if (rawIndices.Offset.Normal != null && j - i == (int)rawIndices.Offset.Normal)
               {
                  indicesContainer.Normals.Add(rawIndices.RawIndices[j]);
               }

               else if (rawIndices.Offset.UV0 != null && j - i == (int)rawIndices.Offset.UV0)
               {
                  indicesContainer.UV0.Add(rawIndices.RawIndices[j]);
               }

               else if (rawIndices.Offset.UV1 != null && j - i == (int)rawIndices.Offset.UV1)
               {
                  indicesContainer.UV1.Add(rawIndices.RawIndices[j]);
               }

               else if (rawIndices.Offset.UV2 != null && j - i == (int)rawIndices.Offset.UV2)
               {
                  indicesContainer.UV2.Add(rawIndices.RawIndices[j]);
               }

               else if (rawIndices.Offset.UV3 != null && j - i == (int)rawIndices.Offset.UV3)
               {
                  indicesContainer.UV3.Add(rawIndices.RawIndices[j]);
               }

               else if (rawIndices.Offset.Color != null && j - i == (int)rawIndices.Offset.Color)
               {
                  indicesContainer.Colors.Add(rawIndices.RawIndices[j]);
               }
            }
         }

         rawIndices.RawIndices.Clear();
         return indicesContainer;
      }

      //A face with more than three vertices has to be triangulated
      public bool IsTriangulationRequired(IEnumerable<int> vertexType)
      {
         bool triangulationNeeded = false;
         if (vertexType != null)
         {
            if (vertexType.Any(t => t > 3))
            {
               triangulationNeeded = true;
            }
         }
         return triangulationNeeded;
      }

      internal IndicesContainer Triangulate(VertexSemantic semantic, IndicesContainer indicesContainer,
         List<int> vertexType)
      {
         List<int> triangulatedPositionList = new List<int>();
         List<int> triangulatedNormalList = new List<int>();
         List<int> triangulatedUV0List = new List<int>();
         List<int> triangulatedUV1List = new List<int>();
         List<int> triangulatedUV2List = new List<int>();
         List<int> triangulatedUV3List = new List<int>();
         List<int> triangulatedColorList = new List<int>();

         // A fan: the first three corners of a face make one triangle, and every corner after that makes another
         // with the face's first corner and the previous one. vertexType says how many corners each face has, and
         // n walks the untriangulated stream face by face.
         int n = 0;
         for (int i = 0; i < vertexType.Count; i++)
         {
            for (int j = 0; j < vertexType[i]; j++)
            {
               if (j < 3)
               {
                  triangulatedPositionList.Add(indicesContainer.Positions[n + j]);
                  if (semantic.HasFlag(VertexSemantic.Normal))
                  {
                     triangulatedNormalList.Add(indicesContainer.Normals[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV0))
                  {
                     triangulatedUV0List.Add(indicesContainer.UV0[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV1))
                  {
                     triangulatedUV1List.Add(indicesContainer.UV1[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV2))
                  {
                     triangulatedUV2List.Add(indicesContainer.UV2[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV3))
                  {
                     triangulatedUV3List.Add(indicesContainer.UV3[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.Color))
                  {
                     triangulatedColorList.Add(indicesContainer.Colors[n + j]);
                  }
               }
               else
               {
                  triangulatedPositionList.Add(indicesContainer.Positions[n]);
                  triangulatedPositionList.Add(indicesContainer.Positions[n + j - 1]);
                  triangulatedPositionList.Add(indicesContainer.Positions[n + j]);

                  if (semantic.HasFlag(VertexSemantic.Normal))
                  {
                     triangulatedNormalList.Add(indicesContainer.Normals[n]);
                     triangulatedNormalList.Add(indicesContainer.Normals[n + j - 1]);
                     triangulatedNormalList.Add(indicesContainer.Normals[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV0))
                  {
                     triangulatedUV0List.Add(indicesContainer.UV0[n]);
                     triangulatedUV0List.Add(indicesContainer.UV0[n + j - 1]);
                     triangulatedUV0List.Add(indicesContainer.UV0[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV1))
                  {
                     triangulatedUV1List.Add(indicesContainer.UV1[n]);
                     triangulatedUV1List.Add(indicesContainer.UV1[n + j - 1]);
                     triangulatedUV1List.Add(indicesContainer.UV1[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV2))
                  {
                     triangulatedUV2List.Add(indicesContainer.UV2[n]);
                     triangulatedUV2List.Add(indicesContainer.UV2[n + j - 1]);
                     triangulatedUV2List.Add(indicesContainer.UV2[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.UV3))
                  {
                     triangulatedUV3List.Add(indicesContainer.UV3[n]);
                     triangulatedUV3List.Add(indicesContainer.UV3[n + j - 1]);
                     triangulatedUV3List.Add(indicesContainer.UV3[n + j]);
                  }

                  if (semantic.HasFlag(VertexSemantic.Color))
                  {
                     triangulatedColorList.Add(indicesContainer.Colors[n]);
                     triangulatedColorList.Add(indicesContainer.Colors[n + j - 1]);
                     triangulatedColorList.Add(indicesContainer.Colors[n + j]);
                  }

               }
            }
            n += vertexType[i];
         }
         indicesContainer.Positions.Clear();
         indicesContainer.Positions = triangulatedPositionList;
         indicesContainer.Normals = triangulatedNormalList;
         indicesContainer.UV0 = triangulatedUV0List;
         indicesContainer.UV1 = triangulatedUV1List;
         indicesContainer.UV2 = triangulatedUV2List;
         indicesContainer.UV3 = triangulatedUV3List;
         indicesContainer.Colors = triangulatedColorList;
         return indicesContainer;
      }
   }
}
