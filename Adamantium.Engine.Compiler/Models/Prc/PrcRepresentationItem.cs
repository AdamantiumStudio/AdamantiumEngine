using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A representation item (PRC_TYPE_RI_*): what it shows - its tessellation, its body in the exact geometry,
/// its points - placed by a coordinate system of the globals, and for a set its items.</summary>
internal sealed class PrcRepresentationItem : PrcTreeEntity
{
    internal int CoordinateSystemIndex { get; set; } = -1;

    internal int TessellationIndex { get; set; } = -1;

    /// <summary>The topological context of its body in the exact geometry section; -1 for none.</summary>
    internal int ContextIndex { get; set; } = -1;

    internal int BodyIndex { get; set; } = -1;

    internal bool IsClosed { get; set; }

    /// <summary>A point set's points, a direction's origin and vector.</summary>
    internal List<Vector3<double>> Points { get; } = [];

    /// <summary>A coordinate system's placement as a row-vector matrix.</summary>
    internal Matrix4x4F Transform { get; set; } = Matrix4x4F.Identity;

    internal List<PrcRepresentationItem> Items { get; } = [];
}
