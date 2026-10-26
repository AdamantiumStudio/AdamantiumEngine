namespace Adamantium.Graphics;

/// <summary>The order a <see cref="SpriteBatch"/> draws its sprites in.</summary>
public enum SpriteSortMode
{
    /// <summary>As they were given.</summary>
    NoSort,

    /// <summary>The deepest - layer depth 1 - first; sprites of one depth as they were given.</summary>
    BackToFront,

    /// <summary>The nearest - layer depth 0 - first; sprites of one depth as they were given.</summary>
    FrontToBack
}
