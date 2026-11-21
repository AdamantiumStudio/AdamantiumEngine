namespace Adamantium.DarkMatter;

/// <summary>One of the 64 layers a simulation's <see cref="PhysicsLayers"/> name: what a body's layer collides with,
/// and which queries meet it. The default is layer 0, every body's unless told.</summary>
public readonly record struct Layer
{
    internal Layer(int index) => Index = index;

    /// <summary>Its place among the 64, 0 for the default.</summary>
    public int Index { get; }
}
