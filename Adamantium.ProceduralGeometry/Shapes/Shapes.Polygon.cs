using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Polygon
        {
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                Vector2<double> radii,
                int tessellation = 40,
                double startAngle = 0,
                Matrix4x4<double>? transform = null)
            {
                if (tessellation < 3)
                {
                    tessellation = 3;
                }

                Mesh mesh;
                switch (geometryType)
                {
                    case GeometryType.Solid:
                        mesh = GenerateSolidGeometry(radii.As<float>(), tessellation, startAngle);
                        break;
                    // BOTH means both: the filled mesh AND the outline contour a stroke reads. It used to fall through to
                    // the outline alone, so anything asking for Both got a line strip and no fill at all - which is what a
                    // filled polygon drawing as a thin ring turned out to be.
                    case GeometryType.Both:
                    {
                        mesh = GenerateSolidGeometry(radii.As<float>(), tessellation, startAngle);
                        mesh.AddContour(Corners(radii, tessellation, startAngle), true);   // a closed loop
                        break;
                    }
                    default:
                        mesh = GenerateOutlinedGeometry(radii.As<float>(), tessellation, startAngle);
                        break;
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            /// <summary>The polygon's corners, corner 0 at <paramref name="startAngle"/> degrees from +x, shared by fill and
            /// outline. The angle offsets the inscribed ellipse's parameter, so a squashed polygon stays in its box.</summary>
            public static List<Vector3<double>> Corners(Vector2<double> radii, int tessellation, double startAngle)
            {
                var start = (float)double.DegreesToRadians(startAngle);
                var points = new List<Vector3<double>>(tessellation);
                for (var i = 0; i < tessellation; ++i)
                {
                    var angle = start + (float)Math.PI * 2 / tessellation * i;
                    points.Add(new Vector3<double>(radii.X * (float)Math.Cos(angle), radii.Y * (float)Math.Sin(angle), 0));
                }

                return points;
            }

            private static Mesh GenerateSolidGeometry(Vector2F diameter, int tessellation, double startAngle)
            {
                var vertices = new List<Vector3<double>>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();
                var center = Vector3<double>.Zero;
                var start = (float)double.DegreesToRadians(startAngle);

                for (int i = 0; i < tessellation; ++i)
                {
                    float angle = start + (float)Math.PI * 2 / tessellation * i;

                    var x = center.X + diameter.X * (float)Math.Cos(angle);
                    var y = center.Y + diameter.Y * (float)Math.Sin(angle);
                    vertices.Add(new Vector3<double>(x, y, 0));
                    var uv = new Vector2F(
                       0.5f + (float)((x - center.X) / (2 * diameter.X)),
                       0.5f + (float)((y - center.Y) / (2 * diameter.Y)));
                    uvs.Add(uv);
                }

                int basicIndex = 0;

                // Clockwise on screen to a camera looking along +Z, the side the GPU takes for the front.
                for (int i = 0; i < tessellation-2; i++)
                {
                    indices.Add(basicIndex);
                    indices.Add(i + 1);
                    indices.Add(i + 2);
                }

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.TriangleList).
                    SetPoints(vertices).
                    SetUVs(0, uvs).
                    SetIndices(indices).
                    CalculateNormals();

                return mesh;
            }

            private static Mesh GenerateOutlinedGeometry(
                Vector2F diameter,
                int tessellation,
                double startAngle)
            {
                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();
                var center = Vector3<double>.Zero;
                var start = (float)double.DegreesToRadians(startAngle);
                int lastIndex = 0;

                for (int i = 0; i <= tessellation; ++i)
                {
                    float angle = start + (float)Math.PI * 2 / tessellation * i;

                    var x = center.X + diameter.X * (float)Math.Cos(angle);
                    var y = center.Y + diameter.Y * (float)Math.Sin(angle);
                    vertices.Add(new Vector3<double>(x, y, 0));
                    indices.Add(lastIndex++);
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
