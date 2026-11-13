using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Compiler.Models.ConversionUtils
{
    public class RawIndicesSemanticData
    {
        public RawIndicesSemanticData()
        {
            SemanticIdMapping = new Dictionary<string, VertexSemantic>();
            RawIndices = new List<int>();
            Offset = new Offset();
            VertexType = new List<int>();
            SmoothingGroups = new List<int>();
            MeshTopology = PrimitiveType.Undefined;
        }

        public VertexSemantic Semantic { get; set; }

        public String MaterialId { get; set; }

        public List<Int32> RawIndices { get; set; }

        public Dictionary<String, VertexSemantic> SemanticIdMapping { get; set; }

        /// <summary>Records whose data a source holds. Records, not Add: two inputs may point at one source
        /// (TEXCOORD with several sets is routine), and Add threw on that.</summary>
        internal void MapSource(String sourceId, VertexSemantic semantic)
        {
            if (!String.IsNullOrEmpty(sourceId)) SemanticIdMapping[sourceId] = semantic;
        }

        public Offset Offset { get; set; }

        public List<int> VertexType { get; set; }

        /// <summary>The smoothing group of each face, beside <see cref="VertexType"/>: 0 for none. Filled by .obj only.
        /// </summary>
        public List<int> SmoothingGroups { get; set; }

        public PrimitiveType MeshTopology { get; set; }
    }
}
