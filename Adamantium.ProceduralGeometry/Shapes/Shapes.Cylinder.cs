using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Cylinder
        {
            // Helper computes a point on a unit circle, aligned to the x/z plane and centered on the origin.
            private static Vector3<double> GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float) (i * 2.0 * Math.PI / tessellation);
                var dx = (float) Math.Sin(angle);
                var dz = (float) Math.Cos(angle);

                return new Vector3<double>(dx, 0, dz);
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double height = 1.0,
                double diameter = 1.0,
                int tessellation = 32,
                Matrix4x4<double>? transform = null)
            {
                if (tessellation < 3)
                    tessellation = 3;

                Mesh mesh;
                if (geometryType == GeometryType.Solid)
                {
                    mesh = GenerateSolidGeometry(height, diameter, tessellation);
                }
                else
                {
                    mesh = GenerateOutlinedGeometry(height, diameter, tessellation);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            /// <summary>A cylinder filling <paramref name="size"/>: <paramref name="size"/>.X wide and .Z deep - an
            /// ellipse when the two differ - and .Y high.</summary>
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                Vector3<double> size,
                int tessellation = 32,
                Matrix4x4<double>? transform = null)
            {
                if (geometryType == GeometryType.Solid)
                {
                    return Cone.GenerateGeometry(GeometryType.Solid, size, 1, tessellation, transform);
                }

                var mesh = GenerateOutlinedGeometry(size.Y, size.X, size.Z / size.X, Math.Max(tessellation, 3));
                mesh.ApplyTransform(transform);
                return mesh;
            }

            public static Mesh GenerateSolidGeometry(
                double height = 1.0,
                double diameter = 1.0,
                int tessellation = 32
                )
            {
                return Cone.GenerateGeometry(GeometryType.Solid, height, diameter, diameter, tessellation);
            }

            public static Mesh GenerateOutlinedGeometry(
                double height = 1.0,
                double diameter = 1.0,
                int tessellation = 32)
            {
                return GenerateOutlinedGeometry(height, diameter, 1, tessellation);
            }

            // The rings stretched along z by depth over width.
            private static Mesh GenerateOutlinedGeometry(
                double height,
                double diameter,
                double depth,
                int tessellation)
            {
                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();
                int lastIndex = 0;
                var radius = new Vector3<double>(diameter / 2, 0, diameter / 2 * depth);
                var topOffset = Vector3<double>.Down * height / 2;

                for (int i = 0; i <= tessellation; ++i)
                {
                    var normal = GetCircleVector(i, tessellation);
                    var sideOffset = normal * radius;
                    vertices.Add(sideOffset - topOffset);
                    indices.Add(lastIndex++);
                }

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

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.LineStrip).
                    SetPoints(vertices).
                    SetIndices(indices);

                return mesh;
            }
        }
    }
}
