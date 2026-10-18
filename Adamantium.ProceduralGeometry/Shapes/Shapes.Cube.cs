using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Cube
        {
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double size,
                int tessellation = 1,
                Matrix4x4? transform = null)
            {
                return GenerateGeometry(geometryType, size, size, size, tessellation, transform);
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double width = 1,
                double height = 1,
                double depth = 1,
                int tessellation = 1,
                Matrix4x4? transform = null)
            {
                if (width <= 0)
                {
                    width = 0.01f;
                }

                if (height <= 0)
                {
                    height = 0.01f;
                }

                if (depth <= 0)
                {
                    depth = 0.01f;
                }

                if (tessellation < 1)
                {
                    tessellation = 1;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(width, height, depth, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(width, height, depth);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                double width = 1,
                double height = 1,
                double depth = 1,
                int tessellation = 1)
            {
                var uvFactor = Vector2F.One;
                var lineWidth = tessellation + 1;
                var quads = (tessellation * tessellation);
                var indices = new List<int>(quads * 36);

                var sizeX = width / 2;
                var sizeY = height / 2;
                var sizeZ = depth / 2;

                var deltaX = width / tessellation;
                var deltaY = height / tessellation;
                var deltaZ = depth / tessellation;

                var front = new List<Vector3>();
                var right = new List<Vector3>();
                var back = new List<Vector3>();
                var left = new List<Vector3>();
                var top = new List<Vector3>();
                var bottom = new List<Vector3>();

                var frontUV = new List<Vector2F>();
                var rightUV = new List<Vector2F>();
                var backUV = new List<Vector2F>();
                var leftUV = new List<Vector2F>();
                var topUV = new List<Vector2F>();
                var bottomUV = new List<Vector2F>();

                //Generate frame
                for (var y = 0; y < lineWidth; y++)
                {
                    for (var x = 0; x < lineWidth; x++)
                    {
                        var pos = new Vector3(-sizeX + deltaX * x, -sizeY + deltaY * y, -sizeZ);
                        var uv = new Vector2F(1.0f - (1.0f * x / tessellation * uvFactor.X), 1.0f - (1.0f * y / tessellation * uvFactor.Y));
                        front.Add(pos);
                        frontUV.Add(uv);
                    }

                    for (var z = 0; z < lineWidth; z++)
                    {
                        var pos = new Vector3(sizeX, -sizeY + deltaY * y, -sizeZ + deltaZ * z);
                        var uv = new Vector2F(1.0f - (1.0f * z / tessellation * uvFactor.X), 1.0f - (1.0f * y / tessellation * uvFactor.Y));
                        right.Add(pos);
                        rightUV.Add(uv);
                    }

                    for (var x = 0; x < lineWidth; x++)
                    {
                        var pos = new Vector3(sizeX - deltaX * x, -sizeY + deltaY * y, sizeZ);
                        var uv = new Vector2F(1.0f - (1.0f * x / tessellation * uvFactor.X), 1.0f - (1.0f * y / tessellation * uvFactor.Y));
                        back.Add(pos);
                        backUV.Add(uv);
                    }

                    for (var z = 0; z < lineWidth; z++)
                    {
                        var pos = new Vector3(-sizeX, -sizeY + deltaY * y, sizeZ - deltaZ * z);
                        var uv = new Vector2F(1.0f - (1.0f * z / tessellation * uvFactor.X), 1.0f - (1.0f * y / tessellation * uvFactor.Y));
                        left.Add(pos);
                        leftUV.Add(uv);
                    }
                }

                //Generate top cap
                for (var z = 0; z < lineWidth; z++)
                {
                    for (var x = 0; x < lineWidth; x++)
                    {
                        var pos = new Vector3(-sizeX + deltaX * x, sizeY, -sizeZ + deltaZ * z);
                        var uv = new Vector2F( 1.0f - (1.0f * x / tessellation * uvFactor.X), 1.0f - (1.0f * z / tessellation * uvFactor.Y));
                        top.Add(pos);
                        topUV.Add(uv);
                    }
                }

                var rot = Quaternion.RotationAxis(Vector3.UnitX, MathHelper.DegreesToRadians(180));
                var rotationMatrix = Matrix4x4.RotationQuaternion(rot);
                //Generate bottom cap
                for (var z = 0; z < lineWidth; z++)
                {
                    for (var x = 0; x < lineWidth; x++)
                    {
                        var pos = new Vector3(-sizeX + deltaX * x, sizeY, -sizeZ + deltaZ * z);
                        var uv = new Vector2F( 1.0f - (1.0f * x / tessellation * uvFactor.X), 1.0f - (1.0f * z / tessellation * uvFactor.Y));
                        pos = Vector3.TransformCoordinate(pos, rotationMatrix);
                        bottom.Add(pos);
                        bottomUV.Add(uv);
                    }
                }

                var allVertices = new List<Vector3>();

                allVertices.AddRange(front);
                allVertices.AddRange(right);
                allVertices.AddRange(back);
                allVertices.AddRange(left);
                allVertices.AddRange(top);
                allVertices.AddRange(bottom);

                var allUVS = new List<Vector2F>();
                allUVS.AddRange(frontUV);
                allUVS.AddRange(rightUV);
                allUVS.AddRange(backUV);
                allUVS.AddRange(leftUV);
                allUVS.AddRange(topUV);
                allUVS.AddRange(bottomUV);

                // Create indices
                /*
                *   0    1
                *
                *   2    3
                */

                var vertexStart = 0;
                for (var i = 0; i < 6; i++)
                {
                    for (var z = 0; z < tessellation; z++)
                    {
                        for (var x = 0; x < tessellation; x++)
                        {
                            // Six indices (two triangles) per face.
                            //1st triangle 
                            var vbase = lineWidth * z + x;
                            indices.Add(vbase + vertexStart);
                            indices.Add(vbase + 1 + vertexStart);
                            indices.Add(vbase + tessellation + 2 + vertexStart);

                            //2nd triangle
                            indices.Add(vbase + vertexStart);
                            indices.Add(vbase + tessellation + 2 + vertexStart);
                            indices.Add(vbase + tessellation + 1 + vertexStart);
                        }
                    }
                    vertexStart += lineWidth * lineWidth;
                }

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.TriangleList).
                    SetPoints(allVertices).
                    SetUVs(0, allUVS).
                    SetIndices(indices).
                    CalculateNormals();

                return mesh;
            }


            private static Mesh GenerateOutlinedGeometry(
                double width = 1,
                double height = 1,
                double depth = 1)
            {
                var startPositionX = -width / 2;
                var startPositionY = -height / 2;
                var startPositionZ = -depth / 2;

                var endPositionX = width / 2;
                var endPositionY = height / 2;
                var endPositionZ = depth / 2;

                var vertices = new List<Vector3>();
                vertices.Add(new Vector3(startPositionX, startPositionY, startPositionZ));
                vertices.Add(new Vector3(startPositionX, endPositionY, startPositionZ));
                vertices.Add(new Vector3(endPositionX, endPositionY, startPositionZ));
                vertices.Add(new Vector3(endPositionX, startPositionY, startPositionZ));
                vertices.Add(new Vector3(startPositionX, startPositionY, endPositionZ));
                vertices.Add(new Vector3(startPositionX, endPositionY, endPositionZ));
                vertices.Add(new Vector3(endPositionX, endPositionY, endPositionZ));
                vertices.Add(new Vector3(endPositionX, startPositionY, endPositionZ));

                // The twelve edges as pairs: a list needs no strip restart, which not every draw turns on.
                List<int> indices =
                [
                    0, 1, 1, 2, 2, 3, 3, 0,
                    4, 5, 5, 6, 6, 7, 7, 4,
                    0, 4, 1, 5, 2, 6, 3, 7
                ];

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.LineList).
                    SetPoints(vertices).
                    SetIndices(indices);

                return mesh;
            }
        }
    }
}
