using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Rendering;

/// <summary>A mesh's wireframe drawn as lines in a frame: where the mesh stands, whether it is drawn over the mesh's own
/// shading, and - for a skinned mesh - where its joints' palette is in the frame's palettes.</summary>
internal readonly record struct MeshWire(
    MeshData Data,
    Matrix4x4F World,
    bool OverShaded,
    uint PaletteFirst,
    uint PaletteCount);
