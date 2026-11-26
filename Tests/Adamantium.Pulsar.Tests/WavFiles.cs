using System.Runtime.InteropServices;
using System.Text;

namespace Adamantium.Pulsar.Tests;

/// <summary>WAV files made of samples.</summary>
internal static class WavFiles
{
    public static byte[] Floats(int rate, int channels, float[] samples)
    {
        byte[] data = [.. MemoryMarshal.AsBytes(samples.AsSpan())];
        byte[] body =
        [
            .. Encoding.ASCII.GetBytes("WAVEfmt "), 16, 0, 0, 0, 3, 0, (byte)channels, 0, .. BitConverter.GetBytes(rate),
            .. BitConverter.GetBytes(rate * channels * 4), (byte)(channels * 4), 0, 32, 0,
            .. Encoding.ASCII.GetBytes("data"), .. BitConverter.GetBytes(data.Length), .. data,
        ];
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(body.Length), .. body];
    }
}
