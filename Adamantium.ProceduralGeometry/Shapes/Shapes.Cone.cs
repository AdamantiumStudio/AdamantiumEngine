using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Cone
        {
            private static readonly Vector3<double> Up = -Vector3<double>.UnitY;

            private static Vector3<double> GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float) (i * 2.0 * Math.PI / tessellation);
                var dx = (float) Math.Sin(angle);
                var dz = (float) Math.Cos(angle);

                return new Vector3<double>(-dx, 0, dz);
            }

            private static Vector3<double> Stretch(Vector3<double> point, double depth) => new(point.X, point.Y, point.Z * depth);

            private static Vector3F StretchNormal(Vector3<double> normal, double depth) =>
                Vector3<double>.Normalize(new Vector3<double>(normal.X, normal.Y, normal.Z / depth)).As<float>();

            private static void AddEnd(
                List<Vector3<double>> positions,
                List<Vector3F> normals,
                List<Vector2F> uvs,
                List<int> indices,
                int tessellation,
                double height,
                double radius,
                double depth,
                bool top)
            {
                var start = positions.Count;
                for (int k = 1; k < tessellation - 1; k++)
                {
                    indices.AddRange(top ? [start, start + k + 1, start + k] : [start, start + k, start + k + 1]);
                }

                var facing = top ? Up : -Up;
                var across = (float)(radius * 2);
                var textureDown = top ? -1.0 : 1.0;
                for (int i = 0; i < tessellation; i++)
                {
                    var point = GetCircleVector(i, tessellation) * radius + facing * height;
                    positions.Add(Stretch(point, depth));
                    normals.Add(facing.As<float>());
                    uvs.Add(new Vector2F(0.5f + (float)(point.X / across), 0.5f + (float)(textureDown * point.Z / across)));
                }
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double height = 1.0,
                double topDiameter = 0.0,
                double bottomDiameter = 1,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                return Generate(geometryType, height, topDiameter, bottomDiameter, 1, tessellation, transform);
            }

            /// <summary>
            /// A cone filling <paramref name="size"/> - its base <paramref name="size"/>.X wide and .Z deep, an ellipse
            /// when the two differ, its height .Y - with a top <paramref name="topRatio"/> of the base: 0 a point, 1 a
            /// cylinder.
            /// </summary>
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                Vector3<double> size,
                double topRatio,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                return Generate(geometryType, size.Y, size.X * topRatio, size.X, size.Z / size.X, tessellation, transform);
            }

            private static Mesh Generate(
                GeometryType geometryType,
                double height,
                double topDiameter,
                double bottomDiameter,
                double depth,
                int tessellation,
                Matrix4x4<double>? transform)
            {
                if (tessellation < 3)
                {
                    tessellation = 3;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(height, topDiameter, bottomDiameter, depth, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(height, topDiameter, bottomDiameter, depth, tessellation);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                double height,
                double topDiameter,
                double bottomDiameter,
                double depth,
                int tessellation)
            {
                PrimitiveType primitiveType = PrimitiveType.TriangleList;

                var positions = new List<Vector3<double>>();
                var normals = new List<Vector3F>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();

                height /= 2;

                var topRadius = topDiameter / 2;
                var bottomRadius = bottomDiameter / 2;

                var topOffset = Up * height;

                for (int i = 0; i <= tessellation; i++)
                {
                    var round = GetCircleVector(i, tessellation);
                    var slope = StretchNormal(round * (height * 2) + Up * (bottomRadius - topRadius), depth);
                    var uv = new Vector2F(i / (float)tessellation, 0);

                    positions.Add(Stretch(round * topRadius + topOffset, depth));
                    normals.Add(slope);
                    uvs.Add(uv);
                    positions.Add(Stretch(round * bottomRadius - topOffset, depth));
                    normals.Add(slope);
                    uvs.Add(uv + Vector2F.UnitY);
                }

                for (int i = 0; i < tessellation; i++)
                {
                    var upper = 2 * i;
                    var lower = upper + 1;
                    indices.AddRange([upper, upper + 2, lower, lower, upper + 2, lower + 2]);
                }

                if (topRadius > 0)
                {
                    AddEnd(positions, normals, uvs, indices, tessellation, height, topRadius, depth, true);
                }

                if (bottomRadius > 0)
                {
                    AddEnd(positions, normals, uvs, indices, tessellation, height, bottomRadius, depth, false);
                }

                var mesh = new Mesh();
                mesh.SetTopology(primitiveType).
                    SetPoints(positions).
                    SetNormals(normals).
                    SetIndices(indices).
                    SetUVs(0, uvs);

                return mesh;
            }

            private static Mesh GenerateOutlinedGeometry(
                double height,
                double topDiameter,
                double bottomDiameter,
                double depth,
                int tessellation)
            {
                var primitiveType = PrimitiveType.LineStrip;

                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();
                var center = Vector3<double>.Zero;
                int lastIndex = 0;
                var rimAtTop = bottomDiameter <= 0;
                var rimRadius = (rimAtTop ? topDiameter : bottomDiameter) / 2;
                var topOffset = Up * height / 2;
                var rimY = rimAtTop ? topOffset.Y : -topOffset.Y;
                for (int i = 0; i <= tessellation; ++i)
                {
                    float angle = (float) Math.PI * 2 / tessellation * i;

                    double x = center.X + rimRadius * (float) Math.Cos(angle);
                    double y = center.Y + rimRadius * (float) Math.Sin(angle);
                    var position = new Vector3<double>(x, rimY, -y * depth);
                    vertices.Add(position);
                    indices.Add(lastIndex++);
                }

                vertices.Add(rimAtTop ? -topOffset : topOffset);

                var vertexCount = tessellation + 1;
                var indexQuantity = indices.Count - 1;
                var quarterIndex = vertexCount / 4;
                var halfIndex = vertexCount / 2;

                indices.Add(++indexQuantity);

                indices.Add(indices[halfIndex]);
                indices.Add(PrimitiveRestartValue);
                indices.Add(indices[quarterIndex]);
                indices.Add(lastIndex);
                indices.Add(indices[quarterIndex + halfIndex]);

                var mesh = new Mesh();
                mesh.SetPoints(vertices).
                    SetIndices(indices).
                    SetTopology(primitiveType);

                return mesh;
            }
        }
    }
}
