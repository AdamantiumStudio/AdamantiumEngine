using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        public class Sphere
        {
            private class UVSphere
            {
                public static Mesh GenerateGeometry(
                    Vector3<double> size,
                    int tessellation = 40)
                {

                    int rings = tessellation;
                    int around = tessellation * 2;
                    int columns = around + 1;

                    var vertices = new List<Vector3<double>>(columns * (rings + 1));
                    var normals = new List<Vector3F>(vertices.Capacity);
                    var uvs = new List<Vector2F>(vertices.Capacity);
                    var indices = new List<int>(rings * around * 6);

                    var radius = size / 2;

                    for (int ring = 0; ring <= rings; ring++)
                    {
                        var latitude = ring * Math.PI / rings - Math.PI / 2;
                        var y = Math.Sin(latitude);
                        var across = Math.Cos(latitude);

                        for (int column = 0; column < columns; column++)
                        {
                            var longitude = column * 2 * Math.PI / around;
                            var direction = new Vector3<double>(across * Math.Sin(longitude), y, across * Math.Cos(longitude));

                            // Stretched by axis, an ellipsoid's normal leans the other way: its direction over the radii.
                            vertices.Add(direction * radius);
                            normals.Add(Vector3<double>.Normalize(direction / radius).As<float>());
                            uvs.Add(new Vector2F(1 - (float)column / around, (float)ring / rings));
                        }
                    }

                    // STOPS ONE SHORT: the last column is the seam, a duplicate of the first. Wrapping it round
                    // with a modulo stitched that column to itself - a whole ring of zero-area triangles whose
                    // normals averaged into the seam and left a dark stripe down the sphere.
                    for (int ring = 0; ring < rings; ring++)
                    {
                        for (int column = 0; column < around; column++)
                        {
                            var here = ring * columns + column;
                            var above = here + columns;

                            indices.AddRange([here, above, here + 1, here + 1, above, above + 1]);
                        }
                    }

                    var mesh = new Mesh();
                    // A sphere's normal IS its direction from the center, so it is written rather than averaged back
                    // out of the triangles: exact at the poles and the seam, where averaging has the least to work with.
                    mesh.SetTopology(PrimitiveType.TriangleList).
                        SetPoints(vertices).
                        SetNormals(normals).
                        SetUVs(0, uvs).
                        SetIndices(indices);

                    return mesh;
                }
            }

            private class GeoSphere
            {
                public static readonly PrimitiveType PrimitiveType = PrimitiveType.TriangleList;

                /// <summary>An octahedron - its corners on the axes - whose every triangle is split into four
                /// <paramref name="tessellation"/> times, the new corners pushed out onto the sphere.</summary>
                public static Mesh GenerateGeometry(
                    Vector3<double> size,
                    int tessellation = 3)
                {
                    var radius = size / 2.0;
                    var (vertexPositions, indexList) = Octahedron();

                    for (int split = 0; split < tessellation; split++)
                    {
                        var midpoints = new Dictionary<(int, int), int>();
                        int Midpoint(int a, int b)
                        {
                            var edge = (Math.Min(a, b), Math.Max(a, b));
                            if (!midpoints.TryGetValue(edge, out var middle))
                            {
                                middle = vertexPositions.Count;
                                vertexPositions.Add((vertexPositions[a] + vertexPositions[b]) * 0.5);
                                midpoints[edge] = middle;
                            }

                            return middle;
                        }

                        var finer = new List<int>(indexList.Count * 4);
                        for (int t = 0; t < indexList.Count; t += 3)
                        {
                            int a = indexList[t], b = indexList[t + 1], c = indexList[t + 2];
                            var ab = Midpoint(a, b);
                            var bc = Midpoint(b, c);
                            var ca = Midpoint(a, c);

                            finer.AddRange([a, ab, ca, ca, bc, c, ca, ab, bc, ab, b, bc]);
                        }

                        indexList = finer;
                    }

                    var directions = new List<Vector3<double>>(vertexPositions.Count);
                    var points = new List<Vector3<double>>(vertexPositions.Count);
                    var normals = new List<Vector3F>(vertexPositions.Count);
                    var uvs = new List<Vector2F>(vertexPositions.Count);
                    foreach (var position in vertexPositions)
                    {
                        var direction = Vector3<double>.Normalize(position);
                        directions.Add(direction);
                        points.Add(direction * radius);
                        normals.Add(Vector3<double>.Normalize(direction / radius).As<float>());

                        var longitude = Math.Atan2(direction.X, -direction.Z);
                        var latitude = Math.Acos(Math.Clamp(direction.Y, -1, 1));
                        uvs.Add(new Vector2F((float)(longitude / (Math.PI * 2.0) + 0.5), (float)(1.0 - latitude / Math.PI)));
                    }

                    var copies = new Dictionary<(int, float), int>();
                    var finalIndices = new int[indexList.Count];
                    var u = new float[3];
                    for (int i = 0; i < indexList.Count; i += 3)
                    {
                        float open = 0;
                        int openCount = 0;
                        for (int k = 0; k < 3; k++)
                        {
                            var index = indexList[i + k];
                            u[k] = uvs[index].X;
                            if (!OnPole(directions[index]) && !OnSeam(directions[index]))
                            {
                                open += u[k];
                                openCount++;
                            }
                        }

                        var side = openCount > 0 ? open / openCount : 0.5f;
                        for (int k = 0; k < 3; k++)
                        {
                            var direction = directions[indexList[i + k]];
                            if (OnSeam(direction) && !OnPole(direction)) u[k] = side < 0.5f ? 0 : 1;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            if (OnPole(directions[indexList[i + k]])) u[k] = (u[(k + 1) % 3] + u[(k + 2) % 3]) / 2;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            var index = indexList[i + k];
                            var at = i + (3 - k) % 3;
                            if (u[k] == uvs[index].X)
                            {
                                finalIndices[at] = index;
                                continue;
                            }

                            if (!copies.TryGetValue((index, u[k]), out var copy))
                            {
                                copy = points.Count;
                                points.Add(points[index]);
                                normals.Add(normals[index]);
                                uvs.Add(new Vector2F(u[k], uvs[index].Y));
                                copies[(index, u[k])] = copy;
                            }

                            finalIndices[at] = copy;
                        }
                    }

                    var mesh = new Mesh();
                    mesh.SetTopology(PrimitiveType).
                        SetPoints(points).
                        SetNormals(normals).
                        SetUVs(0, uvs).
                        SetIndices(finalIndices);

                    return mesh;
                }

                private static bool OnPole(Vector3<double> direction) => Math.Abs(direction.Y) > 1 - 1e-9;

                private static bool OnSeam(Vector3<double> direction) => Math.Abs(direction.X) < 1e-9 && direction.Z > 0;

                private static (List<Vector3<double>> Points, List<int> Indices) Octahedron()
                {
                    var top = new Vector3<double>(0, 1, 0);
                    var bottom = new Vector3<double>(0, -1, 0);
                    Vector3<double>[] equator = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
                    List<Vector3<double>> points = [top, .. equator, bottom];

                    const int TopIndex = 0;
                    const int BottomIndex = 5;
                    List<int> indices = [];
                    for (int k = 0; k < 4; k++)
                    {
                        indices.AddRange([TopIndex, 1 + (k + 1) % 4, 1 + k]);
                    }

                    for (int k = 3; k >= 0; k--)
                    {
                        indices.AddRange([BottomIndex, 1 + k, 1 + (k + 1) % 4]);
                    }

                    return (points, indices);
                }
            }

            private class CubeSphere
            {
                public static Mesh GenerateGeometry(
                    Vector3<double> size,
                    int tessellation = 3)
                {
                    if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(size), "size must be more than 0 along every axis");
                    }

                    var mesh = Cube.GenerateGeometry(GeometryType.Solid, 1, 1, 1, tessellation, null);

                    var vertices = mesh.Points;
                    var radius = size / 2;

                    for (int i = 0; i < vertices.Length; i++)
                    {
                        double x = 0;
                        double y = 0;
                        double z = 0;

                        double sqrX = 0;
                        double sqrY = 0;
                        double sqrZ = 0;

                        double d = 0;
                        double theta = 0;
                        double phi = 0;

                        x = vertices[i].X;
                        y = vertices[i].Y;
                        z = vertices[i].Z;

                        sqrX = Math.Pow(x, 2);
                        sqrY = Math.Pow(y, 2);
                        sqrZ = Math.Pow(z, 2);

                        d = Math.Sqrt(sqrX + sqrY + sqrZ);

                        theta = Math.Acos(z / d);
                        phi = Math.Atan2(y, x);

                        var position = new Vector3<double>();
                        position.X = (float) (radius.X * Math.Sin(theta) * Math.Cos(phi));
                        position.Y = (float) (radius.Y * Math.Sin(theta) * Math.Sin(phi));
                        position.Z = (float) (radius.Z * Math.Cos(theta));

                        vertices[i] = position;
                    }

                    mesh.SetPoints(vertices).
                        CalculateNormals();

                    return mesh;
                }
            }

            public static Mesh GenerateOutlinedGeometry(
                double diameter = 1.0,
                int tessellation = 40)
            {
                return GenerateOutlinedGeometry(new Vector3<double>(diameter), tessellation);
            }

            /// <summary>Three rings of a sphere stretched to <paramref name="size"/>, one round each axis.</summary>
            public static Mesh GenerateOutlinedGeometry(
                Vector3<double> size,
                int tessellation = 40)
            {
                var vertices = new List<Vector3<double>>();
                var indices = new List<int>();
                var radius = size / 2;

                for (int ring = 0; ring < 3; ring++)
                {
                    var start = vertices.Count;
                    for (int i = 0; i < tessellation; ++i)
                    {
                        var angle = Math.PI * 2 / tessellation * i;
                        var cos = Math.Cos(angle);
                        var sin = Math.Sin(angle);
                        vertices.Add(ring == 0 ? new Vector3<double>(radius.X * cos, 0, radius.Z * sin)
                            : ring == 1 ? new Vector3<double>(radius.X * cos, radius.Y * sin, 0)
                            : new Vector3<double>(0, radius.Y * cos, radius.Z * sin));
                        indices.Add(start + i);
                        indices.Add(start + (i + 1) % tessellation);
                    }
                }

                Mesh mesh = new Mesh();
                mesh.MeshTopology = PrimitiveType.LineList;
                mesh.SetPoints(vertices);
                mesh.SetIndices(indices);

                return mesh;
            }

            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                SphereType sphereType,
                double diameter = 1.0f,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                return GenerateGeometry(geometryType, sphereType, new Vector3<double>(diameter), tessellation, transform);
            }

            /// <summary>A sphere stretched to <paramref name="size"/> along each axis - an ellipsoid, with its normals
            /// generated for that shape.</summary>
            public static Mesh GenerateGeometry(
                GeometryType geometryType,
                SphereType sphereType,
                Vector3<double> size,
                int tessellation = 40,
                Matrix4x4<double>? transform = null)
            {
                if (tessellation < 3 && sphereType != SphereType.GeoSphere)
                {
                    tessellation = 3;
                }

                if (sphereType == SphereType.GeoSphere && geometryType == GeometryType.Solid && tessellation > 7)
                {
                    tessellation = 7;
                }

                if (sphereType == SphereType.GeoSphere && tessellation < 0)
                {
                    tessellation = 0;
                }

                Mesh mesh;
                if (geometryType == GeometryType.Outlined)
                {
                    mesh = GenerateOutlinedGeometry(size, tessellation);
                }
                else
                {
                    switch (sphereType)
                    {
                        case SphereType.GeoSphere:
                            mesh = GeoSphere.GenerateGeometry(size, tessellation);
                            break;
                        case SphereType.CubeSphere:
                            mesh = CubeSphere.GenerateGeometry(size, tessellation);
                            break;
                        default:
                            mesh = UVSphere.GenerateGeometry(size, tessellation);
                            break;
                    }
                }
                
                mesh.ApplyTransform(transform);

                return mesh;
            }
        }
    }
}
