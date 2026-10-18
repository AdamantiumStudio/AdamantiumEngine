using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Ellipse
        {
            public static Mesh GenerateGeometry(
               GeometryType geometryType,
               EllipseType ellipseType,
               Vector2 diameter,
               double startAngle = 0,
               double sweepAngle = 360,
               bool isClockWise = true,
               int tessellation = 36,
               Matrix4x4? transform = null)
            {
                if (sweepAngle is > 360 or < -360)
                {
                    sweepAngle %= 360;
                }

                // A full ellipse, or a pie sector (whose outline returns through the center), is a closed loop; an
                // edge-to-edge ARC is an open polyline. This flag drives MeshContour.IsGeometryClosed, which is what the
                // GPU stroke reads to decide whether to wrap the ribbon back to the first point.
                bool isClosed = Math.Abs(sweepAngle) >= 360.0 || ellipseType == EllipseType.Sector;

                Mesh mesh = null;

                switch (geometryType)
                {
                    case GeometryType.Solid:
                        mesh = GenerateSolidGeometry(ellipseType, diameter, startAngle, sweepAngle, isClockWise, tessellation, transform);
                        break;
                    case GeometryType.Both:
                    {
                        // Fill comes from the dedicated solid generator (a clean triangulation - full disc / pie sector /
                        // segment); the stroke reads the separate outline CONTOUR. Triangulating the outline as the fill
                        // (the old path) garbled partial arcs and depended on a duplicated start vertex - which the clean
                        // outline (needed by the GPU stroke) no longer has.
                        mesh = GenerateSolidGeometry(ellipseType, diameter, startAngle, sweepAngle, isClockWise, tessellation, transform);
                        var contour = GenerateOutlinedGeometry(ellipseType, diameter, startAngle, sweepAngle,
                            isClockWise, tessellation, transform);
                        mesh.AddContour(contour, isClosed);
                        break;
                    }
                    case GeometryType.Outlined:
                    {
                        var contour = GenerateOutlinedGeometry(ellipseType, diameter, startAngle, sweepAngle,
                            isClockWise, tessellation, transform);
                        mesh = new Mesh();
                        mesh.AddContour(contour, isClosed);
                        break;
                    }
                }

                return mesh;
            }

            private static Mesh GenerateSolidGeometry(
                EllipseType ellipseType,
                Vector2 diameter,
                double startAngle = 0,
                double sweepAngle = 360,
                bool isClockWise = true,
                int tessellation = 36,
                Matrix4x4? transform = null)
            {
                var vertices = new List<Vector2>();
                List<Vector2F> uvs = new List<Vector2F>();
                var center = Vector3.Zero;
                var radiusX = diameter.X / 2;
                var radiusY = diameter.Y / 2;

                var range = sweepAngle;
                var angle = range / (tessellation - 1);
                
                float sign = -1;
                if (isClockWise)
                {
                    sign = 1;
                }
                
                startAngle = MathHelper.DegreesToRadians(startAngle);
                var currentAngle = startAngle;

                if (ellipseType == EllipseType.Sector && range < 360)
                {
                    vertices.Add(Vector2.Zero);
                }

                for (int i = 0; i < tessellation; ++i)
                {
                    var angleItem = MathHelper.DegreesToRadians(currentAngle * sign);
                    var x = center.X + (radiusX * Math.Cos(angleItem));
                    var y = center.Y + (radiusY * Math.Sin(angleItem));
                    
                    x = Math.Round(x, 4);
                    y = Math.Round(y, 4);
                    
                    var vertex = new Vector2(x, y);
                        
                    vertices.Add(vertex);

                    currentAngle += angle;
                    if (currentAngle > sweepAngle)
                    {
                        currentAngle = sweepAngle;

                        if (currentAngle == 360)
                        {
                            currentAngle = 0;
                        }
                    }
                }

                var polygon = new Mathematics.Triangulation.Polygon();
                polygon.AddContour(new MeshContour(vertices));
                var points = polygon.FillIndirect();

                for (int i = 0; i < points.Count; ++i)
                {
                    var point = points[i];
                    var uv = new Vector2F(
                        1.0f - (float)(0.5 + (point.X - center.X) / diameter.X),
                        1.0f - (float)(0.5 + (point.Y - center.Y) / diameter.Y));
                    uvs.Add(uv);
                }
                
                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.TriangleList).
                    SetPoints(points).
                    SetIndices(FacingTheEye(points.Count)).
                    SetUVs(0, uvs).
                    Optimize().
                    ApplyTransform(transform);
                
                return mesh;
            }

            private static List<Vector3> GenerateOutlinedGeometry(
                EllipseType ellipseType,
                Vector2 diameter,
                double startAngle = 0,
                double sweepAngle = 360,
                bool isClockWise = true,
                int tessellation = 36,
                Matrix4x4? transform = null)
            {
                var vertices = new List<Vector3>();
                var radiusX = diameter.X / 2;
                var radiusY = diameter.Y / 2;

                // Full ellipse: emit exactly `tessellation` points around it WITHOUT repeating the start vertex - the
                // closing edge is implicit (IsGeometryClosed), and a duplicated start/end vertex is a zero-length segment
                // the GPU stroke can't normalize. Partial arc: emit `tessellation + 1` points so BOTH the start- and
                // stop-angle endpoints land exactly on the arc.
                bool isFull = Math.Abs(sweepAngle) >= 360.0;
                int count = isFull ? tessellation : tessellation + 1;

                // SAME mapping as the solid branch above (isClockWise -> +1). It was inverted here, so one flag meant
                // opposite things in the two halves of this file: an ellipse's OUTLINE wound against its own FILL and
                // against every other shape's outline (the rectangle emits TL->TR->BR->BL = clockwise on screen). The
                // stroke expander walks the contour in order, so dashes, caps and arrow heads ran backwards on ellipses.
                float sign = isClockWise ? 1f : -1f;
                double startRad = MathHelper.DegreesToRadians(startAngle) * sign;
                double stepRad = MathHelper.DegreesToRadians(sweepAngle / tessellation) * sign;

                for (int i = 0; i < count; ++i)
                {
                    double a = startRad + stepRad * i;
                    double x = Math.Round(radiusX * Math.Cos(a), 4, MidpointRounding.AwayFromZero);
                    double y = Math.Round(radiusY * Math.Sin(a), 4, MidpointRounding.AwayFromZero);
                    vertices.Add(new Vector3(x, y, 0));
                }

                // A pie sector's outline runs arc -> center -> back to the first radius (closed loop); an edge-to-edge
                // arc stops at the rim.
                if (!isFull && ellipseType == EllipseType.Sector)
                {
                    vertices.Add(Vector3.Zero);
                }

                if (transform is { IsIdentity: false })
                {
                    vertices = Mesh.ApplyTransform(vertices, transform.Value).ToList();
                }

                return vertices;
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
