namespace Adamantium.Pulsar.Decoding;

/// <summary>An Opus packet's framing (RFC 6716, 3): its TOC byte and its frames; and, as a stream of a multistream
/// packet but the last has it (Appendix B), self-delimited by the last frame's length.</summary>
internal static class OpusPacket
{
    public const int MostFrames = 48;
    private const int LongestFrame = 1275;

    public static OpusMode ModeOf(byte toc) =>
        (toc & 0x80) != 0 ? OpusMode.Celt : (toc & 0x60) == 0x60 ? OpusMode.Hybrid : OpusMode.Silk;

    /// <summary>Of the CELT layer, one past the last band coded.</summary>
    public static int EndBandOf(byte toc)
    {
        int bandwidth;
        if ((toc & 0x80) != 0)
        {
            bandwidth = (toc >> 5) & 3;
            // Narrow, wide, super wide, full.
            return bandwidth switch { 0 => 13, 1 => 17, 2 => 19, _ => 21 };
        }

        if ((toc & 0x60) == 0x60)
        {
            return (toc & 0x10) != 0 ? 21 : 19;
        }

        bandwidth = (toc >> 5) & 3;
        return bandwidth == 0 ? 13 : 17;
    }

    /// <summary>SILK's rate inside: 8, 12 or 16 kHz.</summary>
    public static int SilkRateOf(byte toc) =>
        ModeOf(toc) == OpusMode.Silk ? ((toc >> 5) & 3) switch { 0 => 8000, 1 => 12000, _ => 16000 } : 16000;

    /// <summary>A frame's samples at 48 kHz.</summary>
    public static int FrameSizeOf(byte toc)
    {
        if ((toc & 0x80) != 0)
        {
            return 120 << ((toc >> 3) & 3);
        }

        if ((toc & 0x60) == 0x60)
        {
            return (toc & 0x08) != 0 ? 960 : 480;
        }

        var size = (toc >> 3) & 3;
        return size == 3 ? 2880 : 480 << size;
    }

    public static int ChannelsOf(byte toc) => (toc & 4) != 0 ? 2 : 1;

    /// <summary>The frames, where each begins and how long it is; how many, or -1 for a packet no Opus encoder could
    /// have made. <paramref name="length"/> is how much of <paramref name="packet"/> it was.</summary>
    public static int Parse(ReadOnlySpan<byte> packet, bool isSelfDelimited, Span<int> starts, Span<int> sizes,
        out int length)
    {
        length = 0;
        if (packet.IsEmpty)
        {
            return -1;
        }

        var toc = packet[0];
        var frameSize = FrameSizeOf(toc);
        var at = 1;
        var left = packet.Length - 1;
        var lastSize = left;
        var isConstant = false;
        int count;
        switch (toc & 3)
        {
            case 0:
                count = 1;
                break;
            case 1:
                count = 2;
                isConstant = true;
                if (!isSelfDelimited)
                {
                    if ((left & 1) != 0)
                    {
                        return -1;
                    }

                    sizes[0] = lastSize = left / 2;
                }

                break;
            case 2:
                count = 2;
                var read = SizeOf(packet[at..], out sizes[0]);
                left -= read;
                if (sizes[0] < 0 || sizes[0] > left)
                {
                    return -1;
                }

                at += read;
                lastSize = left - sizes[0];
                break;
            default:
                if (left < 1)
                {
                    return -1;
                }

                var frames = packet[at++];
                count = frames & 0x3F;
                if (count <= 0 || frameSize * count > 5760)
                {
                    return -1;
                }

                left--;
                if ((frames & 0x40) != 0)
                {
                    // RFC 8251, 4: the padding taken from what is left, not summed apart.
                    int padding;
                    do
                    {
                        if (left <= 0)
                        {
                            return -1;
                        }

                        padding = packet[at++];
                        left--;
                        left -= padding == 255 ? 254 : padding;
                    }
                    while (padding == 255);
                }

                if (left < 0)
                {
                    return -1;
                }

                isConstant = (frames & 0x80) == 0;
                if (!isConstant)
                {
                    lastSize = left;
                    for (var frame = 0; frame < count - 1; frame++)
                    {
                        var bytes = SizeOf(packet[at..], out sizes[frame]);
                        left -= bytes;
                        if (sizes[frame] < 0 || sizes[frame] > left)
                        {
                            return -1;
                        }

                        at += bytes;
                        lastSize -= bytes + sizes[frame];
                    }

                    if (lastSize < 0)
                    {
                        return -1;
                    }
                }
                else if (!isSelfDelimited)
                {
                    lastSize = left / count;
                    if (lastSize * count != left)
                    {
                        return -1;
                    }

                    for (var frame = 0; frame < count - 1; frame++)
                    {
                        sizes[frame] = lastSize;
                    }
                }

                break;
        }

        if (isSelfDelimited)
        {
            var bytes = SizeOf(packet[at..], out sizes[count - 1]);
            left -= bytes;
            if (sizes[count - 1] < 0 || sizes[count - 1] > left)
            {
                return -1;
            }

            at += bytes;
            if (isConstant)
            {
                if (sizes[count - 1] * count > left)
                {
                    return -1;
                }

                for (var frame = 0; frame < count - 1; frame++)
                {
                    sizes[frame] = sizes[count - 1];
                }
            }
            // Of what was left before its length was read: the length's own bytes too.
            else if (bytes + sizes[count - 1] > lastSize)
            {
                return -1;
            }
        }
        else
        {
            if (lastSize > LongestFrame)
            {
                return -1;
            }

            sizes[count - 1] = lastSize;
        }

        for (var frame = 0; frame < count; frame++)
        {
            starts[frame] = at;
            at += sizes[frame];
        }

        length = at;
        return count;
    }

    // 3.2.1: a frame's length in one byte or two; -1 where there are not enough.
    private static int SizeOf(ReadOnlySpan<byte> bytes, out int size)
    {
        if (bytes.IsEmpty)
        {
            size = -1;
            return -1;
        }

        if (bytes[0] < 252)
        {
            size = bytes[0];
            return 1;
        }

        if (bytes.Length < 2)
        {
            size = -1;
            return -1;
        }

        size = 4 * bytes[1] + bytes[0];
        return 2;
    }
}

internal enum OpusMode
{
    None,
    Silk,
    Hybrid,
    Celt,
}
