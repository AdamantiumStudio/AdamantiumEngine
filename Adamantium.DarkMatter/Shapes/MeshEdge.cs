namespace Adamantium.DarkMatter.Shapes;

/// <summary>An edge of a mesh by its two corners, the lesser first, as any triangle on it names it.</summary>
internal readonly record struct MeshEdge(int Low, int High);
