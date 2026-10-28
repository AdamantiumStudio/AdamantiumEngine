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

                    int verticalSegments = tessellation;
                    int horizontalSegments = tessellation * 2;

                    var vertices = new Vector3<double>[(verticalSegments + 1) * (horizontalSegments + 1)];
                    var normals = new Vector3F[vertices.Length];
                    var uvs = new Vector2F[vertices.Length];
                    var indices = new int[verticalSegments * horizontalSegments * 6];

                    var radius = size / 2;

                    int vertexCount = 0;
                    // Create rings of vertices at progressively higher latitudes.
                    for (int i = 0; i <= verticalSegments; i++)
                    {
                        float v = 1.0f - (float) i / verticalSegments;

                        var latitude = (float) ((i * Math.PI / verticalSegments) - Math.PI / 2.0);
                        var dy = (float) Math.Sin(latitude);
                        var dxz = (float) Math.Cos(latitude);

                        // Create a single ring of vertices at this latitude.
                        for (int j = 0; j <= horizontalSegments; j++)
                        {
                            float u = (float) j / horizontalSegments;

                            var longitude = (float) (j * 2.0 * Math.PI / horizontalSegments);
                            var dx = (float) Math.Sin(longitude);
                            var dz = (float) Math.Cos(longitude);

                            dx *= dxz;
                            dz *= dxz;

                            var normal = new Vector3<double>(dx, dy, dz);

                            // Stretched by axis, an ellipsoid's normal leans the other way: its direction over the radii.
                            vertices[vertexCount] = normal * radius;
                            normals[vertexCount] = Vector3<double>.Normalize(normal / radius).As<float>();
                            uvs[vertexCount++] = new Vector2F(1.0f - u, 1.0f - v);
                        }
                    }

                    // Fill the index buffer with triangles joining each pair of latitude rings.
                    int stride = horizontalSegments + 1;

                    int indexCount = 0;
                    for (int i = 0; i < verticalSegments; i++)
                    {
                        // STOPS ONE SHORT: the last column is the seam, a duplicate of the first. Wrapping it round
                        // with a modulo stitched that column to itself - a whole ring of zero-area triangles whose
                        // normals averaged into the seam and left a dark stripe down the sphere.
                        for (int j = 0; j < horizontalSegments; j++)
                        {
                            int nextI = i + 1;
                            int nextJ = j + 1;

                            indices[indexCount++] = (i * stride + j);
                            indices[indexCount++] = (nextI * stride + j);
                            indices[indexCount++] = (i * stride + nextJ);

                            indices[indexCount++] = (i * stride + nextJ);
                            indices[indexCount++] = (nextI * stride + j);
                            indices[indexCount++] = (nextI * stride + nextJ);
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

                //--------------------------------------------------------------------------------------
                // Geodesic sphere
                //--------------------------------------------------------------------------------------

                private static readonly Vector3<double>[] OctahedronVertices =
                {
                    // when looking down the negative z-axis (into the screen)
                    new Vector3<double>(0, 1, 0), // 0 top
                    new Vector3<double>(0, 0, -1), // 1 front
                    new Vector3<double>(1, 0, 0), // 2 right
                    new Vector3<double>(0, 0, 1), // 3 back
                    new Vector3<double>(-1, 0, 0), // 4 left
                    new Vector3<double>(0, -1, 0), // 5 bottom
                };

                private static readonly int[] OctahedronIndices = new int[]
                {
                    0, 2, 1,// top front-right face
                    0, 3, 2, // top back-right face
                    0, 4, 3, // top back-left face
                    0, 1, 4, // top front-left face
                    5, 4, 1, // bottom front-left face
                    5, 3, 4, // bottom back-left face
                    5, 2, 3, // bottom back-right face
                    5, 1, 2, // bottom front-right face
                };

                private List<Vector3<double>> vertexPositions;

                private List<int> indexList;

                // Key: an edge
                // Value: the index of the vertex which lies midway between the two vertices pointed to by the key value
                // This map is used to avoid duplicating vertices when subdividing triangles along edges.
                Dictionary<UndirectedEdge, int> subdividedEdges;

                /// <summary>
                /// Creates a Geodesic sphere
                /// </summary>
                /// <param name="diameter">The diameter.</param>
                /// <param name="tessellation">The tessellation.</param>
                /// <returns>A <see cref="Mesh"/> containing all necesseray info in raw format.</returns>
                public static Mesh GenerateGeometry(
                    Vector3<double> size,
                    int tessellation = 3)
                {
                    var sphere = new GeoSphere();
                    return sphere.GenerateSphere(size, tessellation);
                }

                /// <summary>
                /// Creates a Geodesic sphere.
                /// </summary>
                /// <param name="size">The size along each axis.</param>
                /// <param name="tessellation">The tessellation.</param>
                /// <returns>A Geodesic sphere.</returns>
                private Mesh GenerateSphere(
                    Vector3<double> size,
                    int tessellation = 3)
                {
                    subdividedEdges = new Dictionary<UndirectedEdge, int>();

                    var radius = size / 2.0;

                    // Start with an octahedron; copy the data into the vertex/index collection.
                    vertexPositions = new List<Vector3<double>>(OctahedronVertices);
                    indexList = new List<int>(OctahedronIndices);

                    for (int iSubdivision = 0; iSubdivision < tessellation; ++iSubdivision)
                    {
                        // The new index collection after subdivision.
                        var newIndices = new List<int>();
                        subdividedEdges.Clear();

                        int triangleCount = indexList.Count / 3;
                        for (int iTriangle = 0; iTriangle < triangleCount; ++iTriangle)
                        {
                            // For each edge on this triangle, create a new vertex in the middle of that edge.
                            // The winding order of the triangles we output are the same as the winding order of the inputs.

                            // Indices of the vertices making up this triangle
                            int iv0 = indexList[iTriangle * 3 + 0];
                            int iv1 = indexList[iTriangle * 3 + 1];
                            int iv2 = indexList[iTriangle * 3 + 2];

                            // Get the new vertices
                            Vector3<double> v01; // vertex on the midpoint of v0 and v1
                            Vector3<double> v12; // ditto v1 and v2
                            Vector3<double> v20; // ditto v2 and v0
                            int iv01; // index of v01
                            int iv12; // index of v12
                            int iv20; // index of v20

                            // Add/get new vertices and their indices
                            DivideEdge(iv0, iv1, out v01, out iv01);
                            DivideEdge(iv1, iv2, out v12, out iv12);
                            DivideEdge(iv0, iv2, out v20, out iv20);

                            // Add the new indices. We have four new triangles from our original one:
                            //        v0
                            //        o
                            //       /a\
                            //  v20 o---o v01
                            //     /b\c/d\
                            // v2 o---o---o v1
                            //       v12

                            // a
                            newIndices.Add(iv0);
                            newIndices.Add(iv01);
                            newIndices.Add(iv20);

                            // b
                            newIndices.Add(iv20);
                            newIndices.Add(iv12);
                            newIndices.Add(iv2);

                            // d
                            newIndices.Add(iv20);
                            newIndices.Add(iv01);
                            newIndices.Add(iv12);

                            // d
                            newIndices.Add(iv01);
                            newIndices.Add(iv1);
                            newIndices.Add(iv12);
                        }

                        indexList.Clear();
                        indexList.AddRange(newIndices);
                    }

                    // Now that we've completed subdivision, fill in the final vertex collection
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

                // Function that, when given the index of two vertices, creates a new vertex at the midpoint of those vertices.
                private void DivideEdge(
                    int i0,
                    int i1,
                    out Vector3<double> outVertex,
                    out int outIndex)
                {
                    var edge = new UndirectedEdge(i0, i1);

                    // Check to see if we've already generated this vertex
                    if (subdividedEdges.TryGetValue(edge, out outIndex))
                    {
                        // We've already generated this vertex before
                        outVertex = vertexPositions[outIndex]; // and the vertex itself
                    }
                    else
                    {
                        // Haven't generated this vertex before: so add it now
                        outVertex = (vertexPositions[i0] + vertexPositions[i1]) * 0.5f;
                        outIndex = vertexPositions.Count;
                        vertexPositions.Add(outVertex);

                        // Now add it to the map.
                        subdividedEdges[edge] = outIndex;
                    }
                }

                // An undirected edge between two vertices, represented by a pair of indexes into a vertex array.
                // Because this edge is undirected, (a,b) is the same as (b,a).
                private struct UndirectedEdge : IEquatable<UndirectedEdge>
                {
                    public UndirectedEdge(
                        int item1,
                        int item2)
                    {
                        // Makes an undirected edge. Rather than overloading comparison operators to give us the (a,b)==(b,a) property,
                        // we'll just ensure that the larger of the two goes first. This'll simplify things greatly.
                        Item1 = Math.Max(item1, item2);
                        Item2 = Math.Min(item1, item2);
                    }

                    public readonly int Item1;

                    public readonly int Item2;

                    public bool Equals(
                        UndirectedEdge other)
                    {
                        return Item1 == other.Item1 && Item2 == other.Item2;
                    }

                    public override bool Equals(
                        object obj)
                    {
                        if (ReferenceEquals(null, obj))
                            return false;
                        return obj is UndirectedEdge && Equals((UndirectedEdge) obj);
                    }

                    public override int GetHashCode()
                    {
                        unchecked
                        {
                            return (Item1.GetHashCode() * 397) ^ Item2.GetHashCode();
                        }
                    }

                    public static bool operator ==(
                        UndirectedEdge left,
                        UndirectedEdge right)
                    {
                        return left.Equals(right);
                    }

                    public static bool operator !=(
                        UndirectedEdge left,
                        UndirectedEdge right)
                    {
                        return !left.Equals(right);
                    }
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
