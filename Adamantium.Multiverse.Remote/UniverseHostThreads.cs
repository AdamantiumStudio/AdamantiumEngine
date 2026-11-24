namespace Adamantium.Multiverse.Remote;

/// <summary>The names of a universe host's threads a debugger lets run while it holds the game: the link to the viewer,
/// and the drawing of the world where it stands. Every other thread of the game stays held.</summary>
public static class UniverseHostThreads
{
    /// <summary>What the names of the threads of the host's link to its viewer start with.</summary>
    public const string ViewerLink = "Universe viewer link";

    /// <summary>The thread that draws the world where a debugger holds it, asked by the viewer. Held while the game runs:
    /// a debugger holds it again only once it waits, never in the middle of a drawing, which would hold what the game's
    /// own drawing needs.</summary>
    public const string Drawing = "Universe drawing where it stands";

    /// <summary>The method, by its full name, <see cref="Drawing"/> draws in (<c>Universe.DrawWhereItStands</c>): a
    /// debugger holds the thread only once none of its frames is in it.</summary>
    public const string DrawingWorksIn = "Adamantium.Multiverse.Universe.DrawWhereItStands";
}
