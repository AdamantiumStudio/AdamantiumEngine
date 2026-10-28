using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Containers;
using Adamantium.Engine.Compiler.Models.ConversionUtils;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Converter.Parsers
{
    public class ObjFileParser : ModelFileParser
    {
        private List<string> fileContent;

        private String FileName;
        private String materialLibPath;

        private const String MTL = "mtllib ";
        private const String NEW_MTL = "newmtl ";
        private const String USEMTL = "usemtl ";
        private const string POSITION = "v ";
        private const string NORMAL = "vn ";
        private const string UV = "vt ";
        private const string FACES = "f ";

        private readonly ObjDataContainer dataContainer;
        public ObjFileParser(String filePath) : base(filePath)
        {
            dataContainer = new ObjDataContainer(filePath);
            FileName = Path.GetFileNameWithoutExtension(filePath);
        }

        public override DataContainer ParseData(ConversionConfig config)
        {
            if (!config.GeometryEnabled && !config.MaterialsEnabled)
            {
                dataContainer.IsFileValid = false;
                return dataContainer;
            }

            fileContent = new List<string>(File.ReadAllLines(FilePath));
            ParseGeometry();

            if (!config.GeometryEnabled)
            {
                dataContainer.GeometryData = null;
            }

            if (config.MaterialsEnabled)
            {
                ParseMaterials();
            }
            dataContainer.IsFileValid = true;
            return dataContainer;
        }

        private void ParseGeometry()
        {
            var positions = new List<Vector3<double>>();
            var normals = new List<Vector3F>();
            var uvs = new List<Vector2F>();
            positions.Add(Vector3<double>.Zero);
            normals.Add(Vector3F.Zero);
            uvs.Add(Vector2F.Zero);
            ObjMeshData geometryData = null;
            RawIndicesSemanticData semanticData = null;
            Offset offset = new Offset();
            ulong offsetIndex = 0;
            int repeatingIndex = 0;

            for (int i = 0; i < fileContent.Count; i++)
            {
                var line = fileContent[i];
                if (line.StartsWith(MTL))
                {
                    materialLibPath = line.Substring(MTL.Length);
                }

                else if (line.StartsWith("o") || line.StartsWith("g"))
                {
                    var meshName = line.Substring(2);
                    ObjectType type = ObjectType.Object;
                    if (line.StartsWith("g"))
                    {
                        type = ObjectType.Group;
                    }
                    geometryData = new ObjMeshData(type);
                    geometryData.Name = meshName;
                    if (dataContainer.Meshes.ContainsKey(meshName))
                    {
                        meshName += "_" + repeatingIndex;
                        repeatingIndex++;
                        geometryData.Name = meshName;
                    }
                    dataContainer.Meshes.Add(meshName, geometryData);

                    if (String.IsNullOrEmpty(materialLibPath))
                    {
                        semanticData = new RawIndicesSemanticData();
                        geometryData.GeometrySemantic.Add(semanticData);
                        semanticData.Offset = offset;
                    }
                }

                else if (line.StartsWith(POSITION))
                {
                    if (offset.Position == null)
                    {
                        offset.Position = offsetIndex;
                        offsetIndex++;
                    }
                    String values = line.Substring(POSITION.Length).Trim(' ');
                    var v = ParseNumericString(values);
                    positions.Add(new Vector3<double>(v));
                }

                else if (line.StartsWith(NORMAL))
                {
                    if (offset.Normal == null)
                    {
                        offset.Normal = offsetIndex;
                        offsetIndex++;
                    }
                    String values = line.Substring(NORMAL.Length);
                    var n = ParseNumericStringFloat(values);
                    normals.Add(new Vector3F(n));
                }

                else if (line.StartsWith(UV))
                {
                    if (offset.UV0 == null)
                    {
                        offset.UV0 = offsetIndex;
                        offsetIndex++;
                    }
                    String values = line.Substring(UV.Length);
                    var uv = ParseNumericStringFloat(values);
                    uvs.Add(new Vector2F(uv[0], uv[1]));
                }

                else if (line.StartsWith(USEMTL))
                {
                    if (semanticData == null || semanticData.Offset.CommonOffset >= 0)
                    {
                        semanticData = new RawIndicesSemanticData();
                    }
                    semanticData.MaterialId = line.Substring(USEMTL.Length);
                    semanticData.Offset = offset;
                    if (geometryData != null && !geometryData.GeometrySemantic.Contains(semanticData))
                    {
                        geometryData?.GeometrySemantic.Add(semanticData);
                    }
                }

                else if (line.StartsWith(FACES))
                {
                    var corners = ParseFace(line.Substring(FACES.Length), positions.Count, uvs.Count, normals.Count,
                       out var indicesCount, out var layout);

                    // A file with no o/g and no usemtl is perfectly legal - the minimal form every exporter can
                    // write. Faces used to be skipped outright when that left semanticData null, so such a file
                    // imported as an empty model without a word.
                    if (semanticData == null)
                    {
                        semanticData = new RawIndicesSemanticData { Offset = offset };
                        EnsureMesh(ref geometryData).GeometrySemantic.Add(semanticData);
                    }

                    semanticData.RawIndices.AddRange(corners);
                    semanticData.VertexType.Add(indicesCount);
                    if (semanticData.MeshTopology == PrimitiveType.Undefined)
                    {
                        var topology = indicesCount >= 3 ? PrimitiveType.TriangleList : PrimitiveType.LineList;
                        semanticData.MeshTopology = topology;

                        // The layout is what the FACE says, not what the file happens to contain: a file may carry
                        // vn lines while its faces never reference them. Every corner is written as three slots
                        // (position, uv, normal) with 0 where a field is absent, so these offsets always hold.
                        offset.Position = 0;
                        offset.UV0 = layout.HasUV ? 1 : null;
                        offset.Normal = layout.HasNormal ? (ulong?)2 : null;
                        semanticData.Semantic = GetSemantic(offset);
                        offset = new Offset();
                        offsetIndex = 0;
                    }
                }
            }

            dataContainer.GeometryData.Positions = positions;
            dataContainer.GeometryData.Normals = normals;
            dataContainer.GeometryData.UV = uvs;
            dataContainer.Modules |= Modules.Geometry;
        }

        private double[] ParseNumericString(string values)
        {
            return values.Split(' ').Where(s => !string.IsNullOrEmpty(s))
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture.NumberFormat)).ToArray();
        }
        
        private float[] ParseNumericStringFloat(string values)
        {
            return values.Split(' ').Where(s => !string.IsNullOrEmpty(s))
                .Select(s => float.Parse(s, CultureInfo.InvariantCulture.NumberFormat)).ToArray();
        }

        private VertexSemantic GetSemantic(Offset offset)
        {
            VertexSemantic semantic = VertexSemantic.None;
            if (offset.Position.HasValue)
            {
                semantic |= VertexSemantic.Position;
            }
            if (offset.Normal.HasValue)
            {
                semantic |= VertexSemantic.Normal;
            }
            if (offset.UV0.HasValue)
            {
                semantic |= VertexSemantic.UV0;
            }
            return semantic;
        }

        //A file that never names an object or a group still has one mesh - the file itself
        private ObjMeshData EnsureMesh(ref ObjMeshData geometryData)
        {
            if (geometryData != null) return geometryData;

            geometryData = new ObjMeshData(ObjectType.Object) { Name = FileName };
            dataContainer.Meshes.Add(geometryData.Name, geometryData);
            return geometryData;
        }

        //Which of the three fields a face corner actually named
        private readonly struct FaceLayout
        {
            public FaceLayout(bool hasUV, bool hasNormal)
            {
                HasUV = hasUV;
                HasNormal = hasNormal;
            }

            public readonly bool HasUV;
            public readonly bool HasNormal;
        }

        /// <summary>Reads one face into three slots per corner - position, uv, normal - with 0 where the corner
        /// left a field out. Splitting on '/' with RemoveEmptyEntries used to collapse the gap in "1//1", so a
        /// position-and-normal face produced two numbers per corner while the offsets assumed three, and every
        /// index after the first shifted.</summary>
        private List<int> ParseFace(string face, int positionCount, int uvCount, int normalCount,
            out int indicesCount, out FaceLayout layout)
        {
            var indicesList = new List<int>();
            var corners = face.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            indicesCount = 0;
            var hasUV = false;
            var hasNormal = false;

            foreach (var corner in corners)
            {
                var fields = corner.Split('/');
                indicesList.Add(Resolve(fields.ElementAtOrDefault(0), positionCount));

                var uv = Resolve(fields.ElementAtOrDefault(1), uvCount);
                var normal = Resolve(fields.ElementAtOrDefault(2), normalCount);
                indicesList.Add(uv);
                indicesList.Add(normal);

                hasUV |= uv > 0;
                hasNormal |= normal > 0;
                indicesCount++;
            }

            layout = new FaceLayout(hasUV, hasNormal);
            return indicesList;
        }

        //An .obj index is 1-based, and a negative one counts back from the newest element
        private static int Resolve(string field, int count)
        {
            if (String.IsNullOrEmpty(field)
                || !Int32.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                return 0;
            }

            return index < 0 ? Math.Max(0, count + index) : index;
        }

        private void ParseMaterials()
        {
            if (String.IsNullOrEmpty(materialLibPath))
            {
                return; // no materials file present for current .obj file
            }

            String path = Path.Combine(Path.GetDirectoryName(FilePath), materialLibPath);
            if (!File.Exists(path))
                return; //defined material file does not exists

            String[] materials = File.ReadAllLines(path);
            ObjMaterialData objMaterial = null;
            for (int i = 0; i < materials.Length; i++)
            {
                var line = materials[i];
                if (line.StartsWith(NEW_MTL))
                {
                    var materialName = line.Substring(NEW_MTL.Length);
                    objMaterial = new ObjMaterialData();
                    objMaterial.Name = materialName;
                    dataContainer.Materials.Add(materialName, objMaterial);
                }
                else if (!String.IsNullOrEmpty(line))
                {
                    objMaterial?.Data.Add(line);
                }
            }

            dataContainer.Modules |= Modules.Materials;

        }
    }
}
