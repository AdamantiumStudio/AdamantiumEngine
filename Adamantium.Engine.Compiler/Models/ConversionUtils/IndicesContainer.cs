using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Compiler.Models.ConversionUtils
{
   //Holds the per-semantic index arrays the geometry is assembled from, each in face order
   internal class IndicesContainer
   {
      public IndicesContainer()
      {
         Positions = new List<int>();
         Normals = new List<int>();
         UV0 = new List<int>();
         UV1 = new List<int>();
         UV2 = new List<int>();
         UV3 = new List<int>();
         Colors = new List<int>();
         SmoothingGroups = new List<int>();
      }

      public List<int> Positions { get; set; }

      public List<int> Normals { get; set; }

      public List<int> UV0 { get; set; }

      public List<int> UV1 { get; set; }

      public List<int> UV2 { get; set; }

      public List<int> UV3 { get; set; }

      public List<int> Colors { get; set; }

      /// <summary>The smoothing group of each triangle, 0 for none; empty when the file names no groups.</summary>
      public List<int> SmoothingGroups { get; set; }

      public VertexSemantic Semantic { get; set; }

      public String MaterialId { get; set; }

      public PrimitiveType MeshTopology { get; set; }

   }
}
