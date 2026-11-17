namespace Adamantium.Engine.Rendering;

/// <summary>A copy of a mesh blended over what is behind it, waiting for the frame's far-to-near order.</summary>
internal readonly record struct BlendedMeshDraw(MeshDrawKey Key, MeshInstance Instance);
