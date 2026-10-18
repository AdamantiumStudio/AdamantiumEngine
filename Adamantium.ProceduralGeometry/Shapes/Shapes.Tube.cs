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

            private static Vector3 Stretch(Vector3 point, double depth) => new(point.X, point.Y, point.Z * depth);

            private static void CreateCylinder(
                List<Vector3> vertices,
                List<Vector2F> uvs,
                List<int> indices,
                double radius,
                double height,
                double depth,
                int tessellation,
                bool isInnerCylinder
            )
            {
                height /= 2;
                var topOffset = Vector3.UnitY * height;
                int stride = tessellation + 1;
                int vbase = vertices.Count;
                // Create a ring of triangles around the outside of the cylinder.
                for (int i = 0; i <= tessellation; i++)
                {
                    var normal = GetCircleVector(i, tessellation);

                    var sideOffset = normal * radius;

                    var uv = new Vector2F((float)i / tessellation, 0);

                    vertices.Add(Stretch(sideOffset + topOffset, depth));
                    uvs.Add(uv);
                    vertices.Add(Stretch(sideOffset - topOffset, depth));
                    uvs.Add(uv + Vector2F.UnitY);

                    int index = 0;
                    if (isInnerCylinder)
                    {
                        indices.Add(i * 2 + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(i * 2 + 1 + vbase);
                        indices.Add(index + vbase);

                        indices.Add(i * 2 + 1 + vbase);
                        index = (i * 2 + 3) % (stride * 2);
                        indices.Add(index + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                    }
                    else
                    {
                        indices.Add(i * 2 + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                        indices.Add(i * 2 + 1 + vbase);

                        indices.Add(i * 2 + 1 + vbase);
                        
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                        
                        index = (i * 2 + 3) % (stride * 2);
                        indices.Add(index + vbase);
                    }
                }
            }

            private static void CreateTubeCap(
                List<Vector3> vertices,
                List<Vector2F> uvs,
                List<int> indices,
                double radius,
                double height,
                double thickness,
                double depth,
                int tessellation,
                bool isTop
            )
            {
                height /= 2;
                int vbase = vertices.Count;
                var stride = tessellation + 1;

                // Which end of the cylinder is this?
                var normal = Vector3.UnitY;
                var textureScale = new Vector2F(-0.5f);

                if (!isTop)
                {
                    normal = -normal;
                    textureScale.X = -textureScale.X;
                }

                // Create cap vertices.
                for (int i = 0; i <= tessellation; i++)
                {
                    var circleVector = GetCircleVector(i, tessellation);

                    var position = (circleVector * (radius + thickness)) + (normal * height);
                    var textureCoordinate = new Vector2F(
                        (float)circleVector.X * textureScale.X + 0.5f,
                        (float)circleVector.Z * textureScale.Y + 0.5f);

                    vertices.Add(Stretch(position, depth));
                    uvs.Add(textureCoordinate);

                    position = (circleVector * radius) + (normal * height);
                    textureCoordinate = new Vector2F(
                        (float)(circleVector.X * textureScale.X + 0.5f - (1 / radius)),
                        (float)(circleVector.Z * textureScale.Y + 0.5f - (1 / radius)));

                    vertices.Add(Stretch(position, depth));
                    uvs.Add(textureCoordinate);

                    int index = 0;
                    if (isTop)
                    {
                        indices.Add(i * 2 + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(i * 2 + 1 + vbase);
                        indices.Add(index + vbase);

                        indices.Add(i * 2 + 1 + vbase);
                        index = (i * 2 + 3) % (stride * 2);
                        indices.Add(index + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                    }
                    else
                    {
                        indices.Add(i * 2 + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                        indices.Add(i * 2 + 1 + vbase);

                        indices.Add(i * 2 + 1 + vbase);
                        index = (i * 2 + 2) % (stride * 2);
                        indices.Add(index + vbase);
                        index = (i * 2 + 3) % (stride * 2);
                        indices.Add(index + vbase);
                    }
                }
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double diameter,
                double height,
                double thickness,
                int tessellation = 36,
                Matrix4x4? transform = null)
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
                Vector3 size,
                double thicknessRatio,
                int tessellation = 36,
                Matrix4x4? transform = null)
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

                var vertices = new List<Vector3>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();
                CreateCylinder(vertices, uvs, indices, radius, height, depth, tessellation, true);
                CreateCylinder(vertices, uvs, indices, radius + thickness, height, depth, tessellation, false);

                // Create flat triangle fan caps to seal the top and bottom.
                CreateTubeCap(vertices, uvs, indices, radius, height, thickness, depth, tessellation, true);
                CreateTubeCap(vertices, uvs, indices, radius, height, thickness, depth, tessellation, false);

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

                var vertices = new List<Vector3>();
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
                List<Vector3> vertices,
                List<int> indices,
                double radius,
                double height,
                int tessellation = 40)
            {
                int lastIndex = vertices.Count;
                var topOffset = Vector3.UnitY * height / 2;

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
                List<Vector3> vertices,
                List<int> indices,
                double radius,
                double height,
                double thickness,
                int tessellation,
                bool isTop)
            {
                height /= 2;
                int lastIndex = vertices.Count;
                var normal = Vector3.UnitY;

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
