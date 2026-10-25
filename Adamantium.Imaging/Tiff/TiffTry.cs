using System;
using System.IO;

namespace Adamantium.Imaging.Tiff;

/// <summary>Reading what a TIFF file may hold broken, where a broken part is passed over rather than failing the file.
/// </summary>
internal static class TiffTry
{
    /// <summary>What <paramref name="read"/> gives, or <paramref name="fallback"/> when what it reads is broken or not
    /// read (<see cref="InvalidDataException"/>, <see cref="NotSupportedException"/>).</summary>
    public static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return fallback;
        }
    }
}
