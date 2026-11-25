using System.Security.Cryptography;
using System.Text;

namespace Adamantium.Multiverse.Remote;

/// <summary>The names of a universe host's two pipes, after the name it serves on: one carries what the viewer sends,
/// the other what the host sends. Short whatever that name is: on macOS a pipe is a socket in the user's temporary folder,
/// whose path may have 104 bytes.</summary>
internal static class UniversePipes
{
    private const int HashBytes = 8;

    public static string ToHost(string pipeName) => Short(pipeName) + ".to-host";

    public static string ToViewer(string pipeName) => Short(pipeName) + ".to-viewer";

    private static string Short(string pipeName) =>
        "adm-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pipeName)).AsSpan(0, HashBytes));
}
