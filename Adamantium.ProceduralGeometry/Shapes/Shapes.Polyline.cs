using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ProceduralGeometry.Shapes
{
    public partial class Shapes
    {
        // TODO: this class should be improved to make seamless transitions between points
        // TODO: integrate StrokeGeometry in Graphics layer
        public class Polyline
        {
            /// <summary>A band <paramref name="thickness"/> wide along <paramref name="points"/>, a quad a segment, facing
            /// a camera looking along +Z; its picture runs along the whole line, across it from its top edge.</summary>
            public static Mesh GenerateGeometry(Vector2[] points, float thickness, Matrix4x4? transform = null)
            {
                var vertexArray = new List<Vector3>();
                var indicesArray = new List<int>();
                var uvs = new List<Vector2F>();

                var length = 0.0;
                for (int i = 0; i < points.Length - 1; ++i)
                {
                    length += (points[i + 1] - points[i]).Length();
                }

                var travelled = 0.0;
                for (int i = 0; i < points.Length - 1; ++i)
                {
                    var startPoint = points[i];
                    var endPoint = points[i + 1];
                    var direction = endPoint - startPoint;
                    var normal = new Vector2(-direction.Y, direction.X);
                    normal.Normalize();
                    var half = normal * (thickness * 0.5);

                    var start = vertexArray.Count;
                    vertexArray.Add(new Vector3(startPoint - half, 0));
                    vertexArray.Add(new Vector3(endPoint - half, 0));
                    vertexArray.Add(new Vector3(startPoint + half, 0));
                    vertexArray.Add(new Vector3(endPoint + half, 0));

                    var from = length > 0 ? (float)(travelled / length) : 0;
                    travelled += direction.Length();
                    var to = length > 0 ? (float)(travelled / length) : 0;
                    uvs.Add(new Vector2F(from, 0));
                    uvs.Add(new Vector2F(to, 0));
                    uvs.Add(new Vector2F(from, 1));
                    uvs.Add(new Vector2F(to, 1));

                    // Clockwise on screen to a camera looking along +Z, the side the GPU takes for the front.
                    indicesArray.AddRange([start, start + 1, start + 2, start + 2, start + 1, start + 3]);
                }

                var mesh = new Mesh();
                mesh.SetTopology(PrimitiveType.TriangleList).
                    SetPoints(vertexArray).
                    SetUVs(0, uvs).
                    SetIndices(indicesArray).
                    CalculateNormals().
                    ApplyTransform(transform);

                return mesh;
            }
        }
    }
}
