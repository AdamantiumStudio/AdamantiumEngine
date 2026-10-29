using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>What a meshopt triangle stream is decoded against (Appendix A, mode 1): the next new vertex, the last index
/// given explicitly, and FIFOs of the 16 latest edges and vertices - 0 the latest. Reading an entry never written makes
/// the stream invalid.</summary>
internal sealed class GltfMeshoptTriangleState
{
    private const int Size = 16;

    private readonly uint[] _edges = new uint[2 * Size];
    private readonly uint[] _vertices = new uint[Size];
    private int _edgeHead;
    private int _edgeCount;
    private int _vertexHead;
    private int _vertexCount;

    /// <summary>The next new vertex; wraps at 32 bits.</summary>
    public uint Next { get; set; }

    /// <summary>The last index given explicitly or beside the one before.</summary>
    public uint Last { get; set; }

    /// <summary>The edge <paramref name="age"/> pushes back.</summary>
    public (uint A, uint B) Edge(int age)
    {
        if (age >= _edgeCount)
        {
            throw new InvalidDataException("A meshopt triangle names an edge not yet seen.");
        }

        var at = (_edgeHead - 1 - age) & (Size - 1);
        return (_edges[2 * at], _edges[2 * at + 1]);
    }

    /// <summary>The vertex <paramref name="age"/> pushes back.</summary>
    public uint Vertex(int age)
    {
        if (age >= _vertexCount)
        {
            throw new InvalidDataException("A meshopt triangle names a vertex not yet seen.");
        }

        return _vertices[(_vertexHead - 1 - age) & (Size - 1)];
    }

    public void PushEdge(uint a, uint b)
    {
        (_edges[2 * _edgeHead], _edges[2 * _edgeHead + 1]) = (a, b);
        _edgeHead = (_edgeHead + 1) & (Size - 1);
        if (_edgeCount < Size)
        {
            _edgeCount++;
        }
    }

    public void PushVertex(uint vertex)
    {
        _vertices[_vertexHead] = vertex;
        _vertexHead = (_vertexHead + 1) & (Size - 1);
        if (_vertexCount < Size)
        {
            _vertexCount++;
        }
    }

    /// <summary>The index a zigzag-coded <paramref name="value"/> is a delta to from <see cref="Last"/>, which it
    /// becomes.</summary>
    public uint Delta(uint value)
    {
        Last += (value & 1) != 0 ? ~(value >> 1) : value >> 1;
        return Last;
    }
}
