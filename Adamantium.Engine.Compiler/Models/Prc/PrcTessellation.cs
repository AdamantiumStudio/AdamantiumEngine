namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A tessellation of the tessellation section; its coordinates are x, y, z triples.</summary>
internal abstract class PrcTessellation
{
    internal uint Type { get; init; }

    internal double[] Coordinates { get; set; } = [];
}
