using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        /// <summary>The classic teapot of Martin Newell's data: cubic Bézier patches of 16 control points for one half of
        /// it, each drawn again mirrored left to right, and those of the round parts front to back as well.</summary>
        public class Teapot
        {
            private static readonly (bool RoundFrontToBack, int[] Points)[] Patches =
            [
                (true, [102, 103, 104, 105, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]),
                (true, [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27]),
                (true, [24, 25, 26, 27, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40]),
                (true, [96, 96, 96, 96, 97, 98, 99, 100, 101, 101, 101, 101, 0, 1, 2, 3]),
                (true, [0, 1, 2, 3, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117]),
                (false, [41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56]),
                (false, [53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 28, 65, 66, 67]),
                (false, [68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83]),
                (false, [80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95]),
                (true, [118, 118, 118, 118, 124, 122, 119, 121, 123, 126, 125, 120, 40, 39, 38, 37]),
            ];

            private static readonly Vector3<double>[] TeapotControlPoints = {
                    new (0, 0.345f, -0.05f),
                    new (-0.028f, 0.345f, -0.05f),
                    new (-0.05f, 0.345f, -0.028f),
                    new (-0.05f, 0.345f, -0),
                    new (0, 0.3028125f, -0.334375f),
                    new (-0.18725f, 0.3028125f, -0.334375f),
                    new (-0.334375f, 0.3028125f, -0.18725f),
                    new (-0.334375f, 0.3028125f, -0),
                    new (0, 0.3028125f, -0.359375f),
                    new (-0.20125f, 0.3028125f, -0.359375f),
                    new (-0.359375f, 0.3028125f, -0.20125f),
                    new (-0.359375f, 0.3028125f, -0),
                    new (0, 0.27f, -0.375f),
                    new (-0.21f, 0.27f, -0.375f),
                    new (-0.375f, 0.27f, -0.21f),
                    new (-0.375f, 0.27f, -0),
                    new (0, 0.13875f, -0.4375f),
                    new (-0.245f, 0.13875f, -0.4375f),
                    new (-0.4375f, 0.13875f, -0.245f),
                    new (-0.4375f, 0.13875f, -0),
                    new (0, 0.007499993f, -0.5f),
                    new (-0.28f, 0.007499993f, -0.5f),
                    new (-0.5f, 0.007499993f, -0.28f),
                    new (-0.5f, 0.007499993f, -0),
                    new (0, -0.105f, -0.5f),
                    new (-0.28f, -0.105f, -0.5f),
                    new (-0.5f, -0.105f, -0.28f),
                    new (-0.5f, -0.105f, -0),
                    new (0, -0.105f, 0.5f),
                    new (0, -0.2175f, -0.5f),
                    new (-0.28f, -0.2175f, -0.5f),
                    new (-0.5f, -0.2175f, -0.28f),
                    new (-0.5f, -0.2175f, -0),
                    new (0, -0.27375f, -0.375f),
                    new (-0.21f, -0.27375f, -0.375f),
                    new (-0.375f, -0.27375f, -0.21f),
                    new (-0.375f, -0.27375f, -0),
                    new (0, -0.2925f, -0.375f),
                    new (-0.21f, -0.2925f, -0.375f),
                    new (-0.375f, -0.2925f, -0.21f),
                    new (-0.375f, -0.2925f, -0),
                    new (0, 0.17625f, 0.4f),
                    new (-0.075f, 0.17625f, 0.4f),
                    new (-0.075f, 0.2325f, 0.375f),
                    new (0, 0.2325f, 0.375f),
                    new (0, 0.17625f, 0.575f),
                    new (-0.075f, 0.17625f, 0.575f),
                    new (-0.075f, 0.2325f, 0.625f),
                    new (0, 0.2325f, 0.625f),
                    new (0, 0.17625f, 0.675f),
                    new (-0.075f, 0.17625f, 0.675f),
                    new (-0.075f, 0.2325f, 0.75f),
                    new (0, 0.2325f, 0.75f),
                    new (0, 0.12f, 0.675f),
                    new (-0.075f, 0.12f, 0.675f),
                    new (-0.075f, 0.12f, 0.75f),
                    new (0, 0.12f, 0.75f),
                    new (0, 0.06375f, 0.675f),
                    new (-0.075f, 0.06375f, 0.675f),
                    new (-0.075f, 0.007499993f, 0.75f),
                    new (0, 0.007499993f, 0.75f),
                    new (0, -0.04875001f, 0.625f),
                    new (-0.075f, -0.04875001f, 0.625f),
                    new (-0.075f, -0.09562501f, 0.6625f),
                    new (0, -0.09562501f, 0.6625f),
                    new (-0.075f, -0.105f, 0.5f),
                    new (-0.075f, -0.18f, 0.475f),
                    new (0, -0.18f, 0.475f),
                    new (0, 0.02624997f, -0.425f),
                    new (-0.165f, 0.02624997f, -0.425f),
                    new (-0.165f, -0.18f, -0.425f),
                    new (0, -0.18f, -0.425f),
                    new (0, 0.02624997f, -0.65f),
                    new (-0.165f, 0.02624997f, -0.65f),
                    new (-0.165f, -0.12375f, -0.775f),
                    new (0, -0.12375f, -0.775f),
                    new (0, 0.195f, -0.575f),
                    new (-0.0625f, 0.195f, -0.575f),
                    new (-0.0625f, 0.17625f, -0.6f),
                    new (0, 0.17625f, -0.6f),
                    new (0, 0.27f, -0.675f),
                    new (-0.0625f, 0.27f, -0.675f),
                    new (-0.0625f, 0.27f, -0.825f),
                    new (0, 0.27f, -0.825f),
                    new (0, 0.28875f, -0.7f),
                    new (-0.0625f, 0.28875f, -0.7f),
                    new (-0.0625f, 0.2934375f, -0.88125f),
                    new (0, 0.2934375f, -0.88125f),
                    new (0, 0.28875f, -0.725f),
                    new (-0.0375f, 0.28875f, -0.725f),
                    new (-0.0375f, 0.298125f, -0.8625f),
                    new (0, 0.298125f, -0.8625f),
                    new (0, 0.27f, -0.7f),
                    new (-0.0375f, 0.27f, -0.7f),
                    new (-0.0375f, 0.27f, -0.8f),
                    new (0, 0.27f, -0.8f),
                    new (0, 0.4575f, -0),
                    new (0, 0.4575f, -0.2f),
                    new (-0.1125f, 0.4575f, -0.2f),
                    new (-0.2f, 0.4575f, -0.1125f),
                    new (-0.2f, 0.4575f, -0),
                    new (0, 0.3825f, -0),
                    new (0, 0.27f, -0.35f),
                    new (-0.196f, 0.27f, -0.35f),
                    new (-0.35f, 0.27f, -0.196f),
                    new (-0.35f, 0.27f, -0),
                    new (0, 0.3075f, -0.1f),
                    new (-0.056f, 0.3075f, -0.1f),
                    new (-0.1f, 0.3075f, -0.056f),
                    new (-0.1f, 0.3075f, -0),
                    new (0, 0.3075f, -0.325f),
                    new (-0.182f, 0.3075f, -0.325f),
                    new (-0.325f, 0.3075f, -0.182f),
                    new (-0.325f, 0.3075f, -0),
                    new (0, 0.27f, -0.325f),
                    new (-0.182f, 0.27f, -0.325f),
                    new (-0.325f, 0.27f, -0.182f),
                    new (-0.325f, 0.27f, -0),
                    new (0, -0.33f, -0),
                    new (-0.1995f, -0.33f, -0.35625f),
                    new (0, -0.31125f, -0.375f),
                    new (0, -0.33f, -0.35625f),
                    new (-0.35625f, -0.33f, -0.1995f),
                    new (-0.375f, -0.31125f, -0),
                    new (-0.35625f, -0.33f, -0),
                    new (-0.21f, -0.31125f, -0.375f),
                    new (-0.375f, -0.31125f, -0.21f),
             };


            private static void AddPatch(
                GeometryType geometryType,
                List<Vector3<double>> vertices,
                List<Vector3F> normals,
                List<Vector2F> uvs,
                List<int> indices,
                int[] points,
                int tessellation,
                Vector3<double> scale,
                bool mirrored)
            {
                var patch = new Vector3<double>[16];
                for (int i = 0; i < patch.Length; i++)
                {
                    patch[i] = TeapotControlPoints[points[i]] * scale;
                }

                AddPatchIndices(geometryType, indices, vertices.Count, tessellation, mirrored);
                AddPatchVertices(patch, tessellation, mirrored, vertices, normals, uvs);
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                double size = 1.0,
                int tessellation = 8,
                Matrix4x4<double>? transform = null)
            {
                return GenerateGeometry(geometryType, new Vector3<double>(size), tessellation, transform);
            }

            /// <summary>The teapot scaled by <paramref name="size"/> along each axis.</summary>
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                Vector3<double> size,
                int tessellation = 8,
                Matrix4x4<double>? transform = null)
            {
                var vertices = new List<Vector3<double>>();
                var normals = new List<Vector3F>();
                var uvs = new List<Vector2F>();
                var indices = new List<int>();

                if (tessellation < 1)
                    throw new ArgumentOutOfRangeException(nameof(tessellation), "tessellation must be > 0");

                // The control points stand along +Y; up is -Y, so y turns over, and z with it to keep the patches'
                // winding.
                var scaleVector = new Vector3<double>(size.X, -size.Y, -size.Z);
                var scaleNegateX = scaleVector;
                scaleNegateX.X = -scaleNegateX.X;
                var scaleNegateZ = scaleVector;
                scaleNegateZ.Z = -scaleNegateZ.Z;
                var scaleNegateXZ = new Vector3<double>(-size.X, -size.Y, size.Z);

                foreach (var (roundFrontToBack, points) in Patches)
                {
                    AddPatch(geometryType, vertices, normals, uvs, indices, points, tessellation, scaleVector, false);
                    AddPatch(geometryType, vertices, normals, uvs, indices, points, tessellation, scaleNegateX, true);

                    if (roundFrontToBack)
                    {
                        AddPatch(geometryType, vertices, normals, uvs, indices, points, tessellation, scaleNegateZ, true);
                        AddPatch(geometryType, vertices, normals, uvs, indices, points, tessellation, scaleNegateXZ, false);
                    }
                }

                var mesh = new Mesh();
                mesh.MeshTopology = PrimitiveType.TriangleList;
                if (geometryType == GeometryType.Outlined)
                {
                    mesh.MeshTopology = PrimitiveType.LineStrip;
                }
                mesh.SetPoints(vertices).
                    SetUVs(0, uvs).
                    SetIndices(indices);
                if (geometryType == GeometryType.Solid)
                {
                    mesh.SetNormals(normals);
                }

                mesh.ApplyTransform(transform);

                return mesh;
            }

            /// <summary>The point of the cubic Bézier curve through <paramref name="p1"/>..<paramref name="p4"/> at
            /// <paramref name="t"/>, 0 to 1.</summary>
            public static Vector3<double> CubicInterpolate(ref Vector3<double> p1, ref Vector3<double> p2, ref Vector3<double> p3, ref Vector3<double> p4, float t)
            {
                var w = Weights(t);
                return p1 * w[0] + p2 * w[1] + p3 * w[2] + p4 * w[3];
            }

            private static double[] Weights(double t)
            {
                var s = 1 - t;
                return [s * s * s, 3 * t * s * s, 3 * t * t * s, t * t * t];
            }

            private static double[] Slopes(double t)
            {
                var s = 1 - t;
                return [-s * s, s * (1 - 3 * t), t * (2 - 3 * t), t * t];
            }

            private static Vector3<double> Blend(Vector3<double>[] patch, double[] across, double[] down)
            {
                var sum = Vector3<double>.Zero;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        sum += patch[row * 4 + column] * (down[row] * across[column]);
                    }
                }

                return sum;
            }

            private static void AddPatchVertices(Vector3<double>[] patch, int tessellation, bool isMirrored, List<Vector3<double>> positions,
                List<Vector3F> normals, List<Vector2F> uvs)
            {
                var first = positions.Count;
                var extent = patch.Max(point => (point - patch[0]).LengthSquared());
                var vanishing = 1e-20 * extent * extent;
                for (int i = 0; i <= tessellation; i++)
                {
                    float u = (float)i / tessellation;

                    for (int j = 0; j <= tessellation; j++)
                    {
                        float v = (float)j / tessellation;
                        var position = PatchPoint(patch, u, v);

                        // The classic teapot is not tidy: at its top and bottom a patch has several control points
                        // in one place, where the tangents vanish. The normal is taken a step inside the patch there.
                        var normal = SurfaceNormal(patch, u, v);
                        if (normal.LengthSquared() <= vanishing)
                        {
                            normal = SurfaceNormal(patch, u < 0.5f ? u + 0.01f : u - 0.01f, v < 0.5f ? v + 0.01f : v - 0.01f);
                        }

                        normal.Normalize();
                        if (isMirrored)
                        {
                            normal = -normal;
                        }

                        positions.Add(position);
                        normals.Add(normal.As<float>());
                        uvs.Add(new Vector2F(isMirrored ? 1.0f - u : u, v));
                    }
                }

                if (ReadsUp(patch, isMirrored))
                {
                    for (var at = first; at < positions.Count; at++)
                    {
                        uvs[at] = new Vector2F(1 - uvs[at].X, 1 - uvs[at].Y);
                    }
                }
            }

            private static Vector3<double> SurfaceNormal(Vector3<double>[] patch, float u, float v) =>
                Vector3<double>.Cross(Blend(patch, Weights(u), Slopes(v)), Blend(patch, Slopes(u), Weights(v)));

            private static Vector3<double> PatchPoint(Vector3<double>[] patch, float u, float v) =>
                Blend(patch, Weights(u), Weights(v));

            private const int ReadingSamples = 16;

            private static bool ReadsUp(Vector3<double>[] patch, bool isMirrored)
            {
                var rise = 0.0;
                for (var i = 0; i <= ReadingSamples; i++)
                {
                    var u = (float)i / ReadingSamples;
                    for (var j = 0; j < ReadingSamples; j++)
                    {
                        var v = (j + 0.5f) / ReadingSamples;
                        var along = PatchPoint(patch, u, v + 0.5f / ReadingSamples) - PatchPoint(patch, u, v - 0.5f / ReadingSamples);
                        var at = PatchPoint(patch, u, v);
                        var radial = new Vector3<double>(at.X, 0, at.Z);
                        var normal = SurfaceNormal(patch, u, v);
                        if (along.LengthSquared() < 1e-12 || radial.LengthSquared() < 1e-12 || normal.LengthSquared() < 1e-12) continue;

                        if (isMirrored) normal = -normal;
                        var facing = Math.Max(0, Vector3<double>.Dot(Vector3<double>.Normalize(normal), Vector3<double>.Normalize(radial)));
                        rise += facing * along.Y / along.Length();
                    }
                }

                return rise < 0;
            }

            private static void AddPatchIndices(GeometryType geometryType, List<int> indices, int start, int tessellation, bool mirrored)
            {
                int columns = tessellation + 1;
                for (int i = 0; i < tessellation; i++)
                {
                    for (int j = 0; j < tessellation; j++)
                    {
                        var corner = start + i * columns + j;
                        var acrossU = corner + columns;
                        var opposite = acrossU + 1;
                        var acrossV = corner + 1;

                        if (geometryType == GeometryType.Solid)
                        {
                            indices.AddRange(mirrored
                                ? [acrossV, opposite, corner, opposite, acrossU, corner]
                                : [corner, acrossU, opposite, corner, opposite, acrossV]);
                        }
                        else
                        {
                            indices.AddRange(mirrored
                                ? [corner, acrossV, opposite, acrossU, PrimitiveRestartValue]
                                : [corner, acrossU, opposite, acrossV, PrimitiveRestartValue]);
                        }
                    }
                }
            }
        }
    }
}
