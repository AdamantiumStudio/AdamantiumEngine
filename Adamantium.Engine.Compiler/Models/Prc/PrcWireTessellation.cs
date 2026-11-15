namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A wire tessellation (PRC_TYPE_TESS_3D_Wire): polylines through its points.</summary>
internal sealed class PrcWireTessellation : PrcTessellation
{
    internal uint[] WireIndices { get; set; } = [];
}
