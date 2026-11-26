namespace Adamantium.Pulsar.Tests;

/// <summary>The FLAC files among the tests' resources.</summary>
internal static class FlacFiles
{
    public const int LongFrames = 120000;

    /// <summary>What <c>long16</c> and <c>longnotable16</c> hold.</summary>
    public static float[] Long { get; } = new FlacSignal(1, 16, LongFrames, 2).ToFloats();

    public static byte[] Read(string name)
    {
        using var resource = typeof(FlacFiles).Assembly.GetManifestResourceStream(name + ".flac");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
