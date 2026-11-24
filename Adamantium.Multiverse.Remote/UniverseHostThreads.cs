namespace Adamantium.Multiverse.Remote;

/// <summary>The names of a universe host's threads a debugger lets run while it holds the game: the link to the viewer,
/// and the drawing of the world where it stands. Every other thread of the game stays held.</summary>
public static class UniverseHostThreads
{
    /// <summary>What the names of the threads of the host's link to its viewer start with.</summary>
    public const string ViewerLink = "Universe viewer link";

    /// <summary>The thread that draws the world where a debugger holds it, as the viewer asks at a stop; while the game
    /// runs it draws nothing.</summary>
    public const string Drawing = "Universe drawing where it stands";
}
