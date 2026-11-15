namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A face a resolution update adds: its shading ID, its winding and its third position.</summary>
internal sealed class U3dNewFace
{
    internal int ShadingId { get; init; }

    /// <summary>Split, new, third when left; new, split, third when right.</summary>
    internal bool IsLeft { get; init; }

    internal int Third { get; init; }
}
