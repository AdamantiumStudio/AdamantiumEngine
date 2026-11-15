namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A face (three corners), a line (two) or a point (one), with the shading ID it is drawn by.</summary>
internal sealed class U3dPrimitive
{
    internal int ShadingId { get; init; }

    internal U3dCorner[] Corners { get; init; }
}
