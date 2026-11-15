using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Points, normals, colors and indices of one mesh being built from PRC tessellation.</summary>
internal sealed class PrcMeshData
{
    private readonly Dictionary<PrcVertexKey, int> _vertexOf = new();

    internal PrcMeshData(PrimitiveType topology)
    {
        Topology = topology;
    }

    internal PrimitiveType Topology { get; }

    internal List<Vector3<double>> Points { get; } = [];

    internal List<Vector3F> Normals { get; } = [];

    internal List<Color> Colors { get; } = [];

    internal List<Vector2F> TextureCoordinates { get; } = [];

    internal List<int> Indices { get; } = [];

    internal bool HasNormals { get; set; } = true;

    internal bool HasColors { get; set; }

    internal bool HasTextureCoordinates { get; set; }

    /// <summary>Adds the vertex <paramref name="key"/> names, once however many corners share it, and its index.</summary>
    internal void AddCorner(PrcVertexKey key, Vector3<double> point, Vector3F normal, Color color, Vector2F texture)
    {
        if (!_vertexOf.TryGetValue(key, out var vertex))
        {
            vertex = Points.Count;
            _vertexOf.Add(key, vertex);
            Points.Add(point);
            Normals.Add(normal);
            Colors.Add(color);
            TextureCoordinates.Add(texture);
        }

        Indices.Add(vertex);
    }

    /// <summary>Gives each corner of <see cref="Indices"/> its normal from <paramref name="cornerNormals"/>: a vertex
    /// whose corners take different normals is split into one vertex for each.</summary>
    internal void SetCornerNormals(IReadOnlyList<Vector3F> cornerNormals)
    {
        var points = new List<Vector3<double>>(Points);
        var colors = new List<Color>(Colors);
        var textures = new List<Vector2F>(TextureCoordinates);
        var indices = new List<int>(Indices);
        var split = new Dictionary<PrcCornerNormalKey, int>();
        Points.Clear();
        Normals.Clear();
        Colors.Clear();
        TextureCoordinates.Clear();
        Indices.Clear();
        _vertexOf.Clear();
        for (var corner = 0; corner < indices.Count; corner++)
        {
            var old = indices[corner];
            var key = new PrcCornerNormalKey(old, cornerNormals[corner]);
            if (!split.TryGetValue(key, out var vertex))
            {
                vertex = Points.Count;
                split.Add(key, vertex);
                Points.Add(points[old]);
                Normals.Add(cornerNormals[corner]);
                Colors.Add(colors[old]);
                TextureCoordinates.Add(textures[old]);
            }

            Indices.Add(vertex);
        }

        HasNormals = true;
    }

    /// <summary>Lays <paramref name="transformation"/> on every texture coordinate: s and t inverted as it says, then
    /// its matrix.</summary>
    internal void TransformTextureCoordinates(PrcTextureTransformation transformation)
    {
        for (var index = 0; index < TextureCoordinates.Count; index++)
        {
            var coordinate = TextureCoordinates[index];
            var textureS = transformation.FlipS ? 1 - coordinate.X : coordinate.X;
            var textureT = transformation.FlipT ? 1 - coordinate.Y : coordinate.Y;
            var moved = Vector3<double>.TransformCoordinate(new Vector3<double>(textureS, textureT, 0), transformation.Matrix);
            TextureCoordinates[index] = new Vector2F((float)moved.X, (float)moved.Y);
        }
    }
}
