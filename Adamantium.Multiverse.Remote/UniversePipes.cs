namespace Adamantium.Multiverse.Remote;

/// <summary>The names of a universe host's two pipes, after the name it serves on: one carries what the viewer sends,
/// the other what the host sends.</summary>
internal static class UniversePipes
{
    public static string ToHost(string pipeName) => pipeName + ".to-host";

    public static string ToViewer(string pipeName) => pipeName + ".to-viewer";
}
