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
                List<Vector3<double>> vertices = [];
                List<Vector3F> normals = [];
                List<Vector2F> uvs = [];
                List<int> indices = [];

                int ringsPerEnd = tessellation;
                int around = 4 * tessellation;
                int columns = around + 1;
                var quarterTurn = Math.PI / 2;

                for (int ring = 0; ring < 2 * ringsPerEnd; ring++)
                {
                    var upper = ring >= ringsPerEnd;
                    var fromStart = (float)(upper ? ring - ringsPerEnd : ring) / (ringsPerEnd - 1);
                    var latitude = upper ? fromStart * quarterTurn : (fromStart - 1) * quarterTurn;
                    var rise = Math.Sin(latitude);
                    var across = Math.Cos(latitude);
                    var y = radius * rise + (upper ? height / 2 : -height / 2);
                    var v = upper ? 0.25f * (1 - fromStart) : 1 - 0.25f * fromStart;

                    for (int column = 0; column < columns; column++)
                    {
                        var longitude = column * 2 * Math.PI / around;
                        var direction = new Vector3<double>(across * Math.Sin(longitude), rise, across * Math.Cos(longitude));
                        vertices.Add(Stretch(new Vector3<double>(radius * direction.X, y, radius * direction.Z), depth));
                        normals.Add(Vector3<double>.Normalize(new Vector3<double>(direction.X, direction.Y, direction.Z / depth)).As<float>());
                        uvs.Add(new Vector2F(1 - (float)column / around, 1 - v));
                    }
                }

                for (int ring = 0; ring < 2 * ringsPerEnd - 1; ring++)
                {
                    for (int column = 0; column < around; column++)
                    {
                        var here = ring * columns + column;
                        var above = here + columns;

                        indices.Add(here);
                        indices.Add(above);
                        indices.Add(here + 1);

                        indices.Add(here + 1);
                        indices.Add(above);
                        indices.Add(above + 1);
                    }
                }

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.TriangleList).
                    SetPoints(vertices).
                    SetNormals(normals).
                    SetUVs(0, uvs).
                    SetIndices(indices).
                    Optimize(recalculateNormals: false);

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
