using System;
using System.IO;

namespace Adamantium.Core;

/// <summary>Writes a file whole or not at all: into a hidden file beside it first, named afresh so no content glob lists
/// it and no other writer meets it, then moved over the file there was - a failed write leaves that file as it was.
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes <paramref name="path"/> with what <paramref name="write"/> puts in the stream it is given, its
    /// folder made when missing.</summary>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        var writing = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.writing");
        try
        {
            using (var stream = File.Create(writing))
            {
                write(stream);
            }

            File.Move(writing, path, true);
        }
        finally
        {
            File.Delete(writing);
        }
    }
}
