using System;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>The Type 1 encryption: eexec over the private part (key 55665) and the charstring cipher (key 4330), each
/// dropping the random bytes it starts with.</summary>
internal static class Type1Encryption
{
    public const ushort EexecKey = 55665;
    public const ushort CharStringKey = 4330;

    private const ushort C1 = 52845;
    private const ushort C2 = 22719;

    public static byte[] Decrypt(ReadOnlySpan<byte> data, ushort key, int skip)
    {
        if (skip < 0)
        {
            return data.ToArray();
        }

        var r = key;
        var plain = new byte[Math.Max(0, data.Length - skip)];
        for (var i = 0; i < data.Length; i++)
        {
            var cipher = data[i];
            var value = (byte)(cipher ^ (r >> 8));
            r = (ushort)((cipher + r) * C1 + C2);
            if (i >= skip)
            {
                plain[i - skip] = value;
            }
        }

        return plain;
    }
}
