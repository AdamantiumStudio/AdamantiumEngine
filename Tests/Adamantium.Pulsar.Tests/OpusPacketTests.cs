using Adamantium.Pulsar.Decoding;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>The framing of RFC 6716, 3, each code of the TOC byte and what 3.4 says is malformed.</summary>
[TestFixture]
public class OpusPacketTests
{
    // CELT FB 20 ms: config 31.
    private const byte Celt = 31 << 3;

    [Test]
    public void EachCode_GivesItsFrames()
    {
        AssertFrames([Celt, 1, 2, 3], [(1, 3)]);
        AssertFrames([Celt | 1, 1, 2, 3, 4], [(1, 2), (3, 2)]);
        // Code 2: the first frame's length, then the second takes the rest.
        AssertFrames([Celt | 2, 1, 9, 8, 7], [(2, 1), (3, 2)]);
        // Code 3, constant: 3 frames, 2 bytes of padding counted by 2.
        AssertFrames([Celt | 3, 0x40 | 3, 2, 1, 2, 3, 0, 0], [(3, 1), (4, 1), (5, 1)]);
        // Code 3, variable: lengths of the first two, the last the rest.
        AssertFrames([Celt | 3, 0x80 | 3, 1, 2, 5, 6, 7, 8, 9], [(4, 1), (5, 2), (7, 2)]);
        // A length of two bytes: 252 + 4 * 1.
        var long2 = new byte[3 + 256 + 4];
        long2[0] = Celt | 2;
        long2[1] = 252;
        long2[2] = 1;
        AssertFrames(long2, [(3, 256), (259, 4)]);
    }

    [Test]
    public void SelfDelimited_EndsByItsLastFramesLength()
    {
        byte[] packet = [Celt, 2, 7, 8, 99, 99];
        Span<int> starts = stackalloc int[OpusPacket.MostFrames];
        Span<int> sizes = stackalloc int[OpusPacket.MostFrames];

        var count = OpusPacket.Parse(packet, true, starts, sizes, out var length);

        Assert.That(count, Is.EqualTo(1));
        Assert.That((starts[0], sizes[0], length), Is.EqualTo((2, 2, 4)));
    }

    // 3.4: R1 to R7; and padding past what a packet has, as RFC 8251, 4 has it read.
    [Test]
    public void WhatIsMalformed_IsRefused()
    {
        byte[][] packets =
        [
            [],
            [Celt | 1, 1, 2, 3],
            [Celt | 2],
            [Celt | 2, 252],
            [Celt | 2, 5, 1],
            [Celt | 3],
            [Celt | 3, 0],
            // 49 frames of 20 ms: more than 120 ms.
            [Celt | 3, 49],
            [Celt | 3, 3, 1, 2],
            [Celt | 3, 0x40 | 1, 255, 255, 255, 1],
            [Celt | 3, 0x80 | 2, 9, 1],
            [Celt, .. new byte[1276]],
        ];
        Span<int> starts = stackalloc int[OpusPacket.MostFrames];
        Span<int> sizes = stackalloc int[OpusPacket.MostFrames];
        foreach (var packet in packets)
        {
            Assert.That(OpusPacket.Parse(packet, false, starts, sizes, out _), Is.EqualTo(-1), string.Join(",", packet));
        }
    }

    private static void AssertFrames(byte[] packet, (int Start, int Size)[] frames)
    {
        Span<int> starts = stackalloc int[OpusPacket.MostFrames];
        Span<int> sizes = stackalloc int[OpusPacket.MostFrames];

        var count = OpusPacket.Parse(packet, false, starts, sizes, out var length);

        Assert.That(count, Is.EqualTo(frames.Length), string.Join(",", packet));
        for (var frame = 0; frame < count; frame++)
        {
            Assert.That((starts[frame], sizes[frame]), Is.EqualTo(frames[frame]), $"frame {frame}");
        }

        Assert.That(length, Is.LessThanOrEqualTo(packet.Length));
    }
}
