using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Capsule
        {
            
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double height,
                double diameter,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                return Generate(geometryType, height, diameter, 1, tessellation, transform);
            }

            /// <summary>
            /// A capsule filling <paramref name="size"/>: its round ends <paramref name="size"/>.X across, stretched to
            /// .Z deep, and .Y high from end to end - no lower than it is wide.
            /// </summary>
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                Vector3<double> size,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                return Generate(geometryType, Math.Max(size.Y - size.X, 0), size.X, size.Z / size.X, tessellation, transform);
            }

            private static Mesh Generate(
                GeometryType geometryType,
                double height,
                double diameter,
                double depth,
                int tessellation,
                Matrix4x4<double>? transform)
            {
                if (tessellation < 8)
                {
                    tessellation = 8;
                }
                var radius = diameter / 2;
                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(height, radius, depth, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(height, radius, depth, tessellation);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            private static Vector3<double> Stretch(Vector3<double> point, double depth) => new(point.X, point.Y, point.Z * depth);

            private static Mesh GenerateSolidGeometry(
                double height,
                double radius,
                double depth,
                int tessellation)
            {
                var primitiveType = PrimitiveType.TriangleList;
                var vertices = new List<Vector3<double>>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();
                if (tessellation < 3)
                    tessellation = 3;

                float uScale = 1;
                float vScale = 1;

                int verticalSegments = 2 * tessellation;
                int horizontalSegments = 4 * tessellation;

                // Create rings of vertices at progressively higher latitudes.
                for (int i = 0; i < verticalSegments; i++)
                {
                    float v;
                    double deltaY;
                    float latitude;
                    if (i < verticalSegments / 2)
                    {
                        deltaY = -height / 2;
                        v = 1.0f - (0.25f * i / (tessellation - 1));
                        latitude = (float) (i * Math.PI / (verticalSegments - 2) - Math.PI / 2.0);
                    }
                    else
                    {
                        deltaY = height / 2;
                        v = 0.5f - 0.25f * (i - 1) / (tessellation - 1);
                        latitude = (float) ((i - 1) * Math.PI / (verticalSegments - 2) - Math.PI / 2.0);
                    }

                    var dy = (float) Math.Sin(latitude);
                    var dxz = (float) Math.Cos(latitude);


                    // Create a single ring of vertices at this latitude.
                    for (int j = 0; j <= horizontalSegments; j++)
                    {
                        float u = (float) j / horizontalSegments;

                        var longitude = (float) (j * 2.0 * Math.PI / horizontalSegments);
                        var dx = (float) Math.Sin(longitude);
                        var dz = (float) Math.Cos(longitude);

                        dx *= dxz;
                        dz *= dxz;

                        var normal = new Vector3<double>(dx, dy, dz);
                        var uv = new Vector2F(1.0f - (u * uScale), 1.0f - (v * vScale));
                        var position = radius * normal + new Vector3<double>(0, deltaY, 0);
                        vertices.Add(Stretch(position, depth));
                        uvs.Add(uv);
                    }
                }

                // Fill the index buffer with triangles joining each pair of latitude rings.
                int stride = horizontalSegments + 1;

                for (int i = 0; i < verticalSegments - 1; i++)
                {
                    for (int j = 0; j <= horizontalSegments; j++)
                    {
                        int nextI = i + 1;
                        int nextJ = (j + 1) % stride;

                        indices.Add(i * stride + j);
                        indices.Add(nextI * stride + j);
                        indices.Add(i * stride + nextJ);

                        indices.Add(i * stride + nextJ);
                        indices.Add(nextI * stride + j);
                        indices.Add(nextI * stride + nextJ);
                    }
                }

                var mesh = new Mesh();
                mesh.SetTopology(primitiveType).
                    SetPoints(vertices).
                    SetUVs(0, uvs).
                    SetIndices(indices).
                    Optimize();

                return mesh;
            }

            private static Mesh GenerateOutlinedGeometry(
                double height,
                double radius,
                double depth,
                int tessellation)
            {
                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();
                var half = height / 2;

                var profile = new List<Vector3<double>>();
                for (int i = 0; i <= tessellation / 2; ++i)
                {
                    var angle = Math.PI * 2 / tessellation * i;
                    profile.Add(new Vector3<double>(radius * Math.Cos(angle), half + radius * Math.Sin(angle), 0));
                }

                for (int i = tessellation / 2; i <= tessellation; ++i)
                {
                    var angle = Math.PI * 2 / tessellation * i;
                    profile.Add(new Vector3<double>(radius * Math.Cos(angle), -half + radius * Math.Sin(angle), 0));
                }

                var quarter = Matrix4x4.CreateRotationY<double>(float.DegreesToRadians(90));
                var top = new List<Vector3<double>>();
                var bottom = new List<Vector3<double>>();
                for (int i = 0; i < tessellation; ++i)
                {
                    var angle = Math.PI * 2 / tessellation * i;
                    top.Add(new Vector3<double>(radius * Math.Cos(angle), -half, radius * Math.Sin(angle)));
                    bottom.Add(new Vector3<double>(radius * Math.Cos(angle), half, radius * Math.Sin(angle)));
                }

                AddLoop(vertices, indices, profile);
                AddLoop(vertices, indices, profile.Select(point => Vector3<double>.TransformCoordinate(point, quarter)).ToList());
                AddLoop(vertices, indices, top);
                AddLoop(vertices, indices, bottom);

                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = Stretch(vertices[i], depth);
                }

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.LineList).
                    SetPoints(vertices).
                    SetIndices(indices);

                return mesh;
            }

            private static void AddLoop(List<Vector3<double>> vertices, List<int> indices, List<Vector3<double>> loop)
            {
                var start = vertices.Count;
                vertices.AddRange(loop);
                for (int i = 0; i < loop.Count; i++)
                {
                    indices.Add(start + i);
                    indices.Add(start + (i + 1) % loop.Count);
                }
            }
        }
    }
}
