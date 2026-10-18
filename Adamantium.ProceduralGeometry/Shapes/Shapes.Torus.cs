using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Torus
        {
            public static readonly PrimitiveType Topology = PrimitiveType.TriangleList;

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double diameter = 1.0f,
                double thickness = 0.33333,
                int tessellation = 32,
                Matrix4x4? transform = null)
            {
                if (tessellation < 3)
                {
                    tessellation = 3;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(diameter, thickness, Vector3.One, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(diameter, thickness, tessellation);
                }

                if (transform.HasValue)
                {
                    mesh.ApplyTransform(transform.Value);
                }

                return mesh;
            }

            /// <summary>
            /// A solid torus filling <paramref name="size"/>, its tube <paramref name="thicknessRatio"/> of its outer
            /// radius thick: 1 closes the hole.
            /// </summary>
            public static Mesh GenerateGeometry(
                Vector3 size,
                double thicknessRatio,
                int tessellation = 32,
                Matrix4x4? transform = null)
            {
                var tube = size.X / 2 * thicknessRatio;
                var stretch = new Vector3(1, size.Y / tube, size.Z / size.X);
                var mesh = GenerateSolidGeometry(size.X - tube, tube, stretch, Math.Max(tessellation, 3));
                mesh.ApplyTransform(transform);
                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                double diameter,
                double thickness,
                Vector3 stretch,
                int tessellation)
            {
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();

                int stride = tessellation + 1;

                // First we loop around the main ring of the torus.
                for (int i = 0; i <= tessellation; i++)
                {
                    float u = (float)i / tessellation;

                    float outerAngle = i * MathHelper.TwoPi / tessellation - MathHelper.PiOverTwo;

                    //Create a transform matrix that will align geometry to
                    //slice perpendicularly though the current ring position
                    var transformMatrix = Matrix4x4.Translation(diameter / 2, 0, 0) * Matrix4x4.RotationY(outerAngle);

                    // Now we loop along the other axis, around the side of the tube.
                    for (int j = 0; j <= tessellation; j++)
                    {
                        float v = (float) j / tessellation;

                        float innerAngle = j * MathHelper.TwoPi / tessellation + MathHelper.Pi;
                        float dx = (float) Math.Cos(innerAngle);
                        float dy = (float) Math.Sin(innerAngle);

                        //Create a vertex
                        var normal = new Vector3(dx, dy, 0);
                        var position = normal * thickness / 2;
                        var uv = new Vector2F(u, v);

                        Vector3.TransformCoordinate(ref position, ref transformMatrix, out position);

                        vertices.Add(position * stretch);
                        uvs.Add(uv);

                        //Create indices for 2 triangles
                        int nextI = (i + 1) % stride;
                        int nextJ = (j + 1) % stride;

                        indices.Add(i * stride + j);
                        indices.Add(i * stride + nextJ);
                        indices.Add(nextI * stride + j);
                        
                        indices.Add(i * stride + nextJ);
                        indices.Add(nextI * stride + nextJ);
                        indices.Add(nextI * stride + j);
                    }
                }

                var mesh = new Mesh();
                mesh.MeshTopology = PrimitiveType.TriangleList;
                mesh.SetPoints(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetIndices(indices);
                mesh.CalculateNormals();

                return mesh;
            }

            private static Mesh GenerateOutlinedGeometry(
                double diameter = 1.0,
                double thickness = 0.33333,
                int tessellation = 32)
            {
                var radius = diameter / 2;
                var meshes = new List<Mesh>();
                for (int i = 0; i <= tessellation; i++)
                {
                    var meshItem = Polygon.GenerateGeometry(GeometryType.Outlined, new Vector2(thickness/2), tessellation);
                    var angle = (float)(i * 2.0 * Math.PI / tessellation);
                    var normal = GetCircleVector(i, tessellation);
                    var matrix = Matrix4x4.RotationY(MathHelper.DegreesToRadians(90)) * Matrix4x4.RotationY(angle) * Matrix4x4.Translation(normal*radius);
                    meshItem.ApplyTransform(matrix);
                    meshes.Add(meshItem);
                }

                var vertices = new List<Vector3>();
                var indices = new List<int>();
                int lastIndex = 0;
                for (int i = 0; i <= tessellation; ++i)
                {
                    for (int j = 0; j < meshes.Count; ++j)
                    {
                        vertices.Add(meshes[j].Points[i]);
                        indices.Add(lastIndex++);
                    }
                    indices.Add(PrimitiveRestartValue);
                }
                var m = new Mesh();
                m.SetPoints(vertices).SetIndices(indices);
                meshes.Add(m);

                var mesh = new Mesh();
                mesh.MeshTopology = PrimitiveType.LineStrip;
                mesh.Merge(meshes.ToArray());
                
                return mesh;
            }

            // Helper computes a point on a unit circle, aligned to the x/z plane and centered on the origin.
            private static Vector3 GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float)(i * 2.0 * Math.PI / tessellation);
                var dx = (float)Math.Sin(angle);
                var dz = (float)Math.Cos(angle);

                return new Vector3(dx, 0, dz);
            }
        }
    }
}
