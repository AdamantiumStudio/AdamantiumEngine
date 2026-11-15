using System.Collections.Generic;
using Adamantium.Graphics.Core;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The meshes a markup tessellation gives: its lines, its filled triangles and its points.</summary>
internal sealed class PrcMarkupMeshes
{
    internal PrcMeshData Lines { get; } = new(PrimitiveType.LineList) { HasNormals = false, HasColors = true };

    internal PrcMeshData Triangles { get; } = new(PrimitiveType.TriangleList) { HasNormals = false, HasColors = true };

    internal PrcMeshData Points { get; } = new(PrimitiveType.PointList) { HasNormals = false, HasColors = true };

    internal List<PrcMarkupText> Texts { get; } = [];
}
