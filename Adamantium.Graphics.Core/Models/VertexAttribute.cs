namespace Adamantium.Graphics.Core.Models;

/// <summary>Per-vertex data a mesh holds beyond the arrays it names: each vertex's numbers one after another,
/// <see cref="Components"/> of them a vertex.</summary>
public class VertexAttribute
{
    public int Components { get; set; }

    public float[] Values { get; set; } = [];

    public VertexAttribute Clone() => new() { Components = Components, Values = (float[])Values.Clone() };
}
