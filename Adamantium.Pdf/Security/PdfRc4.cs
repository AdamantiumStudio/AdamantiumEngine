using System;

namespace Adamantium.Pdf.Security;

/// <summary>RC4, which PDF's older revisions of the standard security handler encrypt with. The same call encrypts
/// and decrypts.</summary>
internal static class PdfRc4
{
    public static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        Span<byte> state = stackalloc byte[256];
        for (var i = 0; i < 256; i++)
        {
            state[i] = (byte)i;
        }

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        var x = 0;
        var y = 0;
        for (var k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[k] = (byte)(data[k] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        return output;
    }
}
