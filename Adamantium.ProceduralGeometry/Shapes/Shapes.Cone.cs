using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Cone
        {
            // Up is -Y, so the top is at -Y; the circle runs the other way round in z with it, which keeps the triangles
            // facing out.
            private static readonly Vector3 Up = -Vector3.UnitY;

            //Computes a point on a unit circle, aligned to the x/z plane and centered on the origin, starting at the back
            //(+Z), where the side's picture meets itself out of a camera in front's sight.
            private static Vector3 GetCircleVector(
                int i,
                int tessellation)
            {
                var angle = (float) (i * 2.0 * Math.PI / tessellation);
                var dx = (float) Math.Sin(angle);
                var dz = (float) Math.Cos(angle);

                return new Vector3(-dx, 0, dz);
            }

            // A point and its normal of the round shape, as they stand once it is stretched along z by depth over width.
            private static Vector3 Stretch(Vector3 point, double depth) => new(point.X, point.Y, point.Z * depth);

            private static Vector3F StretchNormal(Vector3 normal, double depth) =>
                (Vector3F)Vector3.Normalize(new Vector3(normal.X, normal.Y, normal.Z / depth));

            // Creates a triangle fan to close the end of a cylinder.
            private static void CreateConeCap(
                List<Vector3> positions,
                List<Vector3F> normals,
                List<Vector2F> uvs,
                List<int> indices,
                int tessellation,
                double height,
                double radius,
                double depth,
                bool isTop)
            {
                // Create cap indices.
                for (int i = 0; i < tessellation - 2; i++)
                {
                    int i1 = (i + 1) % tessellation;
                    int i2 = (i + 2) % tessellation;

                    if (isTop)
                    {
                        Utilities.Swap(ref i1, ref i2);
                    }

                    int vbase = positions.Count;
                    indices.Add(vbase);
                    indices.Add(vbase + i1);
                    indices.Add(vbase + i2);
                }

                // Which end of the cylinder is this?
                var normal = Up;
                var downTheTexture = -1.0;

                if (!isTop)
                {
                    normal = -normal;
                    downTheTexture = 1.0;
                }

                var diameter = new Vector2F((float)radius * 2);
                // Create cap vertices.
                for (int i = 0; i < tessellation; i++)
                {
                    var circleVector = GetCircleVector(i, tessellation);
                    var position = (circleVector * radius) + (normal * height);
                    var uv = new Vector2F(
                        0.5f + (float)(position.X / diameter.X),
                        0.5f + (float)(downTheTexture * position.Z / diameter.Y));

                    positions.Add(Stretch(position, depth));
                    // A cap is FLAT: its whole face looks one way, which is also what keeps the rim a hard edge rather
                    // than blending into the side.
                    normals.Add((Vector3F)normal);
                    uvs.Add(uv);
                }
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double height = 1.0,
                double topDiameter = 0.0,
                double bottomDiameter = 1,
                int tessellation = 40,
                Matrix4x4? transform = null)
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
                Vector3 size,
                double topRatio,
                int tessellation = 40,
                Matrix4x4? transform = null)
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
                Matrix4x4? transform)
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

                var positions = new List<Vector3>();
                var normals = new List<Vector3F>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();

                height /= 2;

                var topRadius = topDiameter / 2;
                var bottomRadius = bottomDiameter / 2;

                var topOffset = Up * height;

                int stride = tessellation + 1;

                // Create a ring of triangles around the outside of the cylinder.
                for (int i = 0; i <= tessellation; i++)
                {
                    var circle = GetCircleVector(i, tessellation);

                    var sideOffsetTop = circle * topRadius;
                    var sideOffsetBottom = circle * bottomRadius;

                    // The side's normal is the circle direction LEANED toward the narrow end by as much as the radius
                    // falls away over the height - straight out only when the two radii match and it is a cylinder.
                    // Written rather than averaged back out of the triangles, which a cone's apex cannot survive: every
                    // vertex there is the same point, so the faces meeting it have no area to weigh.
                    var sideNormal = StretchNormal(circle * (height * 2) + Up * (bottomRadius - topRadius), depth);

                    var uv = new Vector2F(i / (float)tessellation, 0);

                    positions.Add(Stretch(sideOffsetTop + topOffset, depth));
                    normals.Add(sideNormal);
                    uvs.Add(uv);
                    positions.Add(Stretch(sideOffsetBottom - topOffset, depth));
                    normals.Add(sideNormal);
                    uvs.Add(uv + Vector2F.UnitY);

                    if (i == tessellation) continue;

                    indices.Add(i * 2);
                    indices.Add((i * 2 + 2) % (stride * 2));
                    indices.Add(i * 2 + 1);

                    indices.Add(i * 2 + 1);
                    indices.Add((i * 2 + 2) % (stride * 2));
                    indices.Add((i * 2 + 3) % (stride * 2));
                }

                // Flat triangle fans to seal the ends - but only an end that HAS a radius. A cone's point is already
                // closed, and capping it produced a fan of zero-area triangles with NaN texture coordinates (the cap's
                // uv divides by the diameter, which is zero there).
                if (topRadius > 0) CreateConeCap(positions, normals, uvs, indices, tessellation, height, topRadius, depth, true);
                if (bottomRadius > 0) CreateConeCap(positions, normals, uvs, indices, tessellation, height, bottomRadius, depth, false);

                var mesh = new Mesh();
                mesh.SetTopology(primitiveType).
                    SetPoints(positions).
                    SetNormals(normals).
                    SetIndices(indices).
                    SetUVs(0, uvs);

                return mesh;
            }

            // The rim stands at the end with a diameter and the point at the other: at the bottom unless only the top
            // has one.
            private static Mesh GenerateOutlinedGeometry(
                double height,
                double topDiameter,
                double bottomDiameter,
                double depth,
                int tessellation)
            {
                var primitiveType = PrimitiveType.LineStrip;

                var vertices = new List<Vector3>();
                var indices = new List<int>();
                var center = Vector3.Zero;
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
                    var position = new Vector3(x, rimY, -y * depth);
                    vertices.Add(position);
                    indices.Add(lastIndex++);
                }

                // The point.
                vertices.Add(rimAtTop ? -topOffset : topOffset);

                var vertexCount = tessellation + 1;
                var indexQuantity = indices.Count - 1;
                var quarterIndex = vertexCount / 4;
                var halfIndex = vertexCount / 2;

                //Add index for top position
                indices.Add(++indexQuantity);

                //Add index for left position
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
