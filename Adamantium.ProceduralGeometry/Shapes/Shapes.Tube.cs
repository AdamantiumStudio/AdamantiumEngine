using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Tube
        {
            private static Vector3<double> GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float)(i * 2.0 * Math.PI / tessellation);
                var dx = (float)Math.Sin(angle);
                var dz = (float)Math.Cos(angle);

                return new Vector3<double>(dx, 0, dz);
            }

            private static Vector3<double> Stretch(Vector3<double> point, double depth) => new(point.X, point.Y, point.Z * depth);

            private static void AddWall(
                List<Vector3<double>> vertices,
                List<Vector2F> uvs,
                List<int> indices,
                double radius,
                double height,
                double depth,
                int tessellation,
                bool inner)
            {
                var top = Vector3<double>.UnitY * height / 2;
                AddBand(vertices, uvs, indices, tessellation, inner, i =>
                {
                    var side = GetCircleVector(i, tessellation) * radius;
                    var across = (float)i / tessellation;
                    var uv = new Vector2F(inner ? across : 1 - across, 0);
                    return (Stretch(side + top, depth), uv + Vector2F.UnitY, Stretch(side - top, depth), uv);
                });
            }

            private static void AddEnd(
                List<Vector3<double>> vertices,
                List<Vector2F> uvs,
                List<int> indices,
                double radius,
                double height,
                double thickness,
                double depth,
                int tessellation,
                bool top)
            {
                var up = top ? 1.0 : -1.0;
                var level = Vector3<double>.UnitY * (up * height / 2);
                var diameter = (radius + thickness) * 2;
                Vector2F MapOnto(Vector3<double> point) =>
                    new(0.5f + (float)(point.X / diameter), 0.5f + (float)(up * point.Z / diameter));

                AddBand(vertices, uvs, indices, tessellation, top, i =>
                {
                    var round = GetCircleVector(i, tessellation);
                    var outer = round * (radius + thickness) + level;
                    var inner = round * radius + level;
                    return (Stretch(outer, depth), MapOnto(outer), Stretch(inner, depth), MapOnto(inner));
                });
            }

            private static void AddBand(
                List<Vector3<double>> vertices,
                List<Vector2F> uvs,
                List<int> indices,
                int tessellation,
                bool reversed,
                Func<int, (Vector3<double> First, Vector2F FirstUV, Vector3<double> Second, Vector2F SecondUV)> column)
            {
                var start = vertices.Count;
                for (int i = 0; i <= tessellation; i++)
                {
                    var (first, firstUV, second, secondUV) = column(i);
                    vertices.Add(first);
                    uvs.Add(firstUV);
                    vertices.Add(second);
                    uvs.Add(secondUV);
                }

                for (int i = 0; i < tessellation; i++)
                {
                    var first = start + 2 * i;
                    var second = first + 1;
                    var nextFirst = first + 2;
                    var nextSecond = first + 3;

                    if (reversed)
                    {
                        indices.AddRange([first, second, nextFirst, second, nextSecond, nextFirst]);
                    }
                    else
                    {
                        indices.AddRange([first, nextFirst, second, second, nextFirst, nextSecond]);
                    }
                }
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double diameter,
                double height,
                double thickness,
                int tessellation = 36,
                Matrix4x4<double>? transform = null)
            {
                if (tessellation < 3)
                {
                    tessellation = 3;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(diameter, height, thickness, 1, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(diameter, height, thickness, tessellation);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            /// <summary>
            /// A solid tube filling <paramref name="size"/> - <paramref name="size"/>.X wide, .Z deep, .Y high - its wall
            /// <paramref name="thicknessRatio"/> of its outer radius thick: 1 closes the hole.
            /// </summary>
            public static Mesh GenerateGeometry(
                Vector3<double> size,
                double thicknessRatio,
                int tessellation = 36,
                Matrix4x4<double>? transform = null)
            {
                var wall = size.X / 2 * thicknessRatio;
                var mesh = GenerateSolidGeometry(size.X - wall * 2, size.Y, wall, size.Z / size.X, Math.Max(tessellation, 3));
                mesh.ApplyTransform(transform);
                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                double diameter,
                double height,
                double thickness,
                double depth,
                int tessellation)
            {
                var primitiveType = PrimitiveType.TriangleList;

                var radius = diameter / 2;

                var vertices = new List<Vector3<double>>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();
                AddWall(vertices, uvs, indices, radius, height, depth, tessellation, true);
                AddWall(vertices, uvs, indices, radius + thickness, height, depth, tessellation, false);
                AddEnd(vertices, uvs, indices, radius, height, thickness, depth, tessellation, true);
                AddEnd(vertices, uvs, indices, radius, height, thickness, depth, tessellation, false);

                var mesh = new Mesh();
                mesh.MeshTopology = primitiveType;
                mesh.SetPoints(vertices);
                mesh.SetIndices(indices);
                mesh.SetUVs(0, uvs);
                mesh.CalculateNormals();

                return mesh;
            }

            private static Mesh GenerateOutlinedGeometry(
                double diameter,
                double height,
                double thickness,
                int tessellation = 40)
            {
                var radius = diameter / 2;

                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();

                GenerateOutlinedCylinder(vertices, indices, radius, height, tessellation);
                GenerateOutlinedCylinder(vertices, indices, radius + thickness, height, tessellation);
                GenerateOutlinedCaps(vertices, indices, radius, height, thickness, tessellation, true);
                GenerateOutlinedCaps(vertices, indices, radius, height, thickness, tessellation, false);

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.LineStrip).
                    SetPoints(vertices).
                    SetIndices(indices);

                return mesh;
            }

            private static void GenerateOutlinedCylinder(
                List<Vector3<double>> vertices,
                List<int> indices,
                double radius,
                double height,
                int tessellation = 40)
            {
                int lastIndex = vertices.Count;
                var topOffset = Vector3<double>.UnitY * height / 2;

                for (int i = 0; i <= tessellation; ++i)
                {
                    var normal = GetCircleVector(i, tessellation);
                    var sideOffset = normal * radius;

                    vertices.Add(sideOffset - topOffset);
                    indices.Add(lastIndex++);
                }

                indices.Add(PrimitiveRestartValue);

                for (int i = 0; i <= tessellation; ++i)
                {
                    var normal = GetCircleVector(i, tessellation);
                    var sideOffset = normal * radius;
                    vertices.Add(sideOffset + topOffset);
                    indices.Add(lastIndex++);
                }

                indices.Add(PrimitiveRestartValue);

                for (int i = 0; i <= tessellation; ++i)
                {
                    var normal = GetCircleVector(i, tessellation);
                    var sideOffset = normal * radius;
                    vertices.Add(sideOffset - topOffset);
                    vertices.Add(sideOffset + topOffset);
                    indices.Add(lastIndex++);
                    indices.Add(lastIndex++);
                    indices.Add(PrimitiveRestartValue);
                }
            }

            private static void GenerateOutlinedCaps(
                List<Vector3<double>> vertices,
                List<int> indices,
                double radius,
                double height,
                double thickness,
                int tessellation,
                bool isTop)
            {
                height /= 2;
                int lastIndex = vertices.Count;
                var normal = Vector3<double>.UnitY;

                if (!isTop)
                {
                    normal = -normal;
                }

                for (int i = 0; i <= tessellation; ++i)
                {
                    var circleVector = GetCircleVector(i, tessellation);
                    var position = (circleVector * (radius + thickness)) + (normal * height);
                    vertices.Add(position);
                    indices.Add(lastIndex++);

                    position = (circleVector * radius) + (normal * height);
                    vertices.Add(position);
                    indices.Add(lastIndex++);

                    indices.Add(PrimitiveRestartValue);
                }
            }
        }
    }
}
