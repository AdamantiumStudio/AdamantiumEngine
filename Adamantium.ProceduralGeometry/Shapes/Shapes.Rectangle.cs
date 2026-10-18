using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Rectangle
        {
            private const float rectangleSector = 90;

            public static Mesh GenerateGeometry(
                GeometryType type,
                double width,
                double height,
                CornerRadius corners,
                int tessellation = 20,
                Matrix4x4? transform = null)
            {
                return GenerateGeometry(type,
                    0,
                    0,
                    width, 
                    height, 
                    corners, 
                    tessellation,
                    transform);
            }
            
            public static Mesh GenerateGeometry(
                GeometryType type,
                double left,
                double top,
                double width,
                double height,
                CornerRadius corners,
                int tessellation = 20,
                Matrix4x4? transform = null)
            {
                var primitiveType = PrimitiveType.TriangleList;
                if (type == GeometryType.Outlined)
                {
                    primitiveType = PrimitiveType.LineStrip;
                }

                // Degenerate (zero/negative width or height) rectangle: return an EMPTY mesh, not null. A 0-size element
                // (e.g. an Auto-width control with no content) must render nothing rather than feed a null mesh into
                // downstream geometry processing (combined-geometry borders / SplitContoursOnSegments) which would NRE.
                if (width <= 0 || height <= 0) return new Mesh(primitiveType);

                var min = Math.Min(width, height);
                ValidateCorners(ref corners, min);

                var vertices = new List<Vector3>();

                var halfWidth = width / 2;
                var halfHeight = height / 2;

                if (corners.TopLeft > 0)
                {
                    var radius = new Vector2(corners.TopLeft);
                    var centerX = -halfWidth + radius.X;
                    var centerY = -halfHeight + radius.Y;
                    GenerateRoundCorner(tessellation, -180, new Vector2(centerX, centerY), radius, vertices);
                }
                else
                {
                    vertices.Add(new Vector3(-halfWidth, -halfHeight));
                }

                if (corners.TopRight > 0)
                {
                    var radius = new Vector2(corners.TopRight);
                    var centerX = halfWidth - radius.X;
                    var centerY = -halfHeight + radius.Y;
                    GenerateRoundCorner(tessellation, 90, new Vector2(centerX, centerY), radius, vertices);
                }
                else
                {
                    vertices.Add(new Vector3(halfWidth, -halfHeight));
                }

                if (corners.BottomRight > 0)
                {
                    var radius = new Vector2(corners.BottomRight);
                    var centerX = halfWidth - radius.X;
                    var centerY = halfHeight - radius.Y;
                    GenerateRoundCorner(tessellation, 0, new Vector2(centerX, centerY), radius, vertices);
                }
                else
                {
                    vertices.Add(new Vector3(halfWidth, halfHeight));
                }

                if (corners.BottomLeft > 0)
                {
                    var radius = new Vector2(corners.BottomLeft);
                    var centerX = -halfWidth + radius.X;
                    var centerY = halfHeight - radius.Y;
                    GenerateRoundCorner(tessellation, -90, new Vector2(centerX, centerY), radius, vertices);
                }
                else
                {
                    vertices.Add(new Vector3(-halfWidth, halfHeight));
                }

                var placed = transform is { IsIdentity: false };
                if (!placed)
                {
                    vertices = vertices.Round().ToList();
                }

                var mesh = new Mesh(primitiveType);
                if (type != GeometryType.Solid)
                {
                    mesh.AddContour(placed ? Mesh.ApplyTransform(vertices, transform.Value) : vertices, true);
                }

                if (type == GeometryType.Outlined)
                {
                    mesh.GenerateBasicIndices();
                    return mesh;
                }

                var points = Triangulate(vertices);
                var uvs = new List<Vector2F>(points.Count);
                foreach (var point in points)
                {
                    uvs.Add(new Vector2F((float)((point.X + halfWidth) / width), (float)((point.Y + halfHeight) / height)));
                }

                mesh.SetPoints(placed ? Mesh.ApplyTransform(points, transform.Value) : points)
                    .SetIndices(FacingTheEye(points.Count))
                    .SetUVs(0, uvs);

                return mesh;
            }

            private static void GenerateRoundCorner(
                int tessellation,
                float startAngle,
                Vector2 center,
                Vector2 radius,
                List<Vector3> vertices)
            {
                var angleItem = -MathHelper.DegreesToRadians(rectangleSector / tessellation);
                var angle = MathHelper.DegreesToRadians(startAngle);
                for (int i = 0; i <= tessellation; ++i)
                {
                    var x = center.X + (radius.X * Math.Cos(angle));
                    var y = center.Y - (radius.Y * Math.Sin(angle));
                    angle += angleItem;
                    vertices.Add( (Vector3)Vector2.Round(x, y, 3));
                }
            }
            
            private static void ValidateCorners(ref CornerRadius corners, double side)
            {
                if (corners.TopLeft < 0)
                {
                    corners.TopLeft = 0;
                }
                if (corners.TopRight < 0)
                {
                    corners.TopRight = 0;
                }
                if (corners.BottomRight < 0)
                {
                    corners.BottomRight = 0;
                }
                if (corners.BottomLeft < 0)
                {
                    corners.BottomLeft = 0;
                }

                var halfSize = side / 2;

                if (corners.TopLeft > halfSize)
                {
                    corners.TopLeft = halfSize;
                }
                
                if (corners.TopRight > halfSize)
                {
                    corners.TopRight = halfSize;
                }
                
                if (corners.BottomRight > halfSize)
                {
                    corners.BottomRight = halfSize;
                }
                
                if (corners.BottomLeft > halfSize)
                {
                    corners.BottomLeft = halfSize;
                }
            }

            private static List<Vector3> Triangulate(List<Vector3> vertices)
            {
                var polygon = new Mathematics.Triangulation.Polygon();
                polygon.AddContour(new MeshContour(vertices));
                var points = polygon.FillIndirect();
                return points;
            }
        }
    }
}
