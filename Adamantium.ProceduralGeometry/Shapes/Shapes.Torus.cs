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
                Matrix4x4<double>? transform = null)
            {
                if (tessellation < 3)
                {
                    tessellation = 3;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(diameter, thickness, Vector3<double>.One, tessellation);
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
                Vector3<double> size,
                double thicknessRatio,
                int tessellation = 32,
                Matrix4x4<double>? transform = null)
            {
                var tube = size.X / 2 * thicknessRatio;
                var stretch = new Vector3<double>(1, size.Y / tube, size.Z / size.X);
                var mesh = GenerateSolidGeometry(size.X - tube, tube, stretch, Math.Max(tessellation, 3));
                mesh.ApplyTransform(transform);
                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                double diameter,
                double thickness,
                Vector3<double> stretch,
                int tessellation)
            {
                var vertices = new List<Vector3<double>>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();

                var ringRadius = diameter / 2;
                var tubeRadius = thickness / 2;
                int columns = tessellation + 1;

                var section = new (double Out, double Up)[columns];
                for (int j = 0; j < columns; j++)
                {
                    var angle = j * MathF.Tau / tessellation + MathF.PI;
                    section[j] = (tubeRadius * Math.Cos(angle), tubeRadius * Math.Sin(angle));
                }

                for (int i = 0; i < columns; i++)
                {
                    var angle = i * MathF.Tau / tessellation - MathF.PI / 2;
                    var outward = new Vector3<double>(Math.Cos(angle), 0, -Math.Sin(angle));
                    var u = 1 - (float)i / tessellation;

                    for (int j = 0; j < columns; j++)
                    {
                        var point = outward * (ringRadius + section[j].Out) + new Vector3<double>(0, section[j].Up, 0);
                        vertices.Add(point * stretch);
                        uvs.Add(new Vector2F(u, (float)j / tessellation));
                    }
                }

                for (int i = 0; i < tessellation; i++)
                {
                    for (int j = 0; j < tessellation; j++)
                    {
                        var here = i * columns + j;
                        var next = here + columns;

                        indices.Add(here);
                        indices.Add(here + 1);
                        indices.Add(next);

                        indices.Add(here + 1);
                        indices.Add(next + 1);
                        indices.Add(next);
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
                    var meshItem = Polygon.GenerateGeometry(GeometryType.Outlined, new Vector2<double>(thickness/2), tessellation);
                    var angle = (float)(i * 2.0 * Math.PI / tessellation);
                    var normal = GetCircleVector(i, tessellation);
                    var matrix = Matrix4x4.CreateRotationY<double>(float.DegreesToRadians(90)) * Matrix4x4.CreateRotationY<double>(angle) * Matrix4x4.CreateTranslation<double>(normal*radius);
                    meshItem.ApplyTransform(matrix);
                    meshes.Add(meshItem);
                }

                var vertices = new List<Vector3<double>>();
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

            private static Vector3<double> GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float)(i * 2.0 * Math.PI / tessellation);
                var dx = (float)Math.Sin(angle);
                var dz = (float)Math.Cos(angle);

                return new Vector3<double>(dx, 0, dz);
            }
        }
    }
}
