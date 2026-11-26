using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Compression;
using NUnit.Framework;

namespace Adamantium.CoreTests;

[TestFixture]
public class PrefixCodeTests
{
    // Deflate's example (RFC 1951 3.2.2): lengths 3 3 3 3 3 2 4 4 give 010 011 100 101 110 00 1110 1111.
    [Test]
    public void CanonicalCodes_AreShorterFirst_AndInTheirOrder()
    {
        Assert.That(PrefixCode.TryCanonical([3, 3, 3, 3, 3, 2, 4, 4], out var words), Is.True);

        Assert.That(words.Select(word => (word.Symbol, Convert.ToString(word.Code, 2).PadLeft(word.Length, '0'))),
            Is.EqualTo(new[] { (0, "010"), (1, "011"), (2, "100"), (3, "101"), (4, "110"), (5, "00"), (6, "1110"), (7, "1111") }));
        Assert.That(PrefixCode.TryCanonical([1, 1, 1], out _), Is.False, "more than one bit tells apart");
        Assert.That(PrefixCode.TryCanonical([0, 33], out _), Is.False);
    }

    // Random codes of lengths up to 32, against a walk a bit at a time, in both orders.
    [TestCase(PrefixBitOrder.HighFirst, 8)]
    [TestCase(PrefixBitOrder.HighFirst, 32)]
    [TestCase(PrefixBitOrder.LowFirst, 8)]
    [TestCase(PrefixBitOrder.LowFirst, 32)]
    public void EveryCodeword_IsDecoded_AsAWalkReadsIt(PrefixBitOrder order, int longest)
    {
        var random = new Random(longest);
        for (var round = 0; round < 50; round++)
        {
            var lengths = Lengths(random, 2 + random.Next(300), longest);
            Assert.That(PrefixCode.TryCanonical(lengths, out var words), Is.True);
            Assert.That(PrefixCode.TryCreate(words, order, out var code), Is.True);
            Assert.That(code.IsComplete, Is.True);
            Assert.That(code.Shortest, Is.EqualTo(lengths.Where(length => length > 0).Min()));

            foreach (var word in words)
            {
                // The codeword, then bits of no matter.
                var tail = (ulong)random.NextInt64();
                var next = order == PrefixBitOrder.HighFirst
                    ? (ulong)word.Code << (64 - word.Length) | (word.Length == 64 ? 0 : tail >> word.Length)
                    : Reversed(word.Code, word.Length) | (tail << word.Length);

                Assert.That(code.Decode(next, out var length), Is.EqualTo(word.Symbol), $"round {round}, {word}");
                Assert.That(length, Is.EqualTo(word.Length));
            }
        }
    }

    [Test]
    public void BitsThatBeginNoCodeword_AreNone()
    {
        // 0, 10: 11 begins none.
        PrefixCode.TryCreate([new PrefixCodeWord(0, 1, 7), new PrefixCodeWord(2, 2, 8)], PrefixBitOrder.HighFirst, out var code);

        Assert.That(code.IsComplete, Is.False);
        Assert.That(code.Decode(0b10UL << 62, out var length), Is.EqualTo(8));
        Assert.That(length, Is.EqualTo(2));
        Assert.That(code.Decode(0b11UL << 62, out length), Is.EqualTo(-1));
        Assert.That(length, Is.Zero);
    }

    [Test]
    public void OneCodewordOfNoBits_IsReadInNone()
    {
        Assert.That(PrefixCode.TryCreate([new PrefixCodeWord(0, 0, 5)], PrefixBitOrder.LowFirst, out var code), Is.True);

        Assert.That(code.Decode(ulong.MaxValue, out var length), Is.EqualTo(5));
        Assert.That(length, Is.Zero);
        Assert.That(code.Shortest, Is.Zero);
    }

    // A code a file gives as it likes: 2^14 codewords of 32 bits, apart from their 14th bit on. Each table is as big
    // as what it holds needs, not 9 bits whatever it holds, which took 170 MB: some hundreds of bytes a codeword, as a
    // tree of a node a bit takes.
    [Test]
    public void LongCodewordsApartEarly_TakeLittle()
    {
        var words = Enumerable.Range(0, 1 << 14)
            .Select(index => new PrefixCodeWord((uint)index << 18 | 0x2AAAA, 32, index)).ToArray();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out var code), Is.True);
        var taken = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(taken, Is.LessThan(16 << 20), "bytes");
        foreach (var word in words.Where((_, index) => index % 97 == 0))
        {
            Assert.That(code.Decode((ulong)word.Code << 32, out var length), Is.EqualTo(word.Symbol));
            Assert.That(length, Is.EqualTo(32));
        }

        Assert.That(code.Decode((ulong)(words[5].Code ^ 1) << 32, out _), Is.EqualTo(-1));
    }

    [Test]
    public void WhatIsNoPrefixCode_IsRefused()
    {
        PrefixCodeWord[][] codes =
        [
            [new(0, 1, 0), new(0, 2, 1)],
            [new(1, 1, 0), new(1, 1, 1)],
            [new(0, 0, 0), new(1, 1, 1)],
            [new(4, 2, 0)],
            [new(0, 33, 0)],
            [new(0, 1, -1)],
            [new(0, 1, 1 << 24)],
            [new(0, 1, 0), new(1, 1, 1), new(1, 2, 2)],
        ];

        foreach (var words in codes)
        {
            Assert.That(PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out _), Is.False, string.Join(", ", words));
        }
    }

    // A complete code's lengths: from one leaf, a random one no deeper than the longest split in two until there are
    // as many as symbols. The deepest leaf is split half the time, so that codes of the longest length come.
    private static int[] Lengths(Random random, int symbols, int longest)
    {
        symbols = Math.Min(symbols, 1 << Math.Min(longest, 10));
        var leaves = new List<int> { 0 };
        while (leaves.Count < symbols)
        {
            var splittable = Enumerable.Range(0, leaves.Count).Where(leaf => leaves[leaf] < longest).ToArray();
            var deepest = splittable.MaxBy(leaf => leaves[leaf]);
            var chosen = random.Next(2) == 0 ? deepest : splittable[random.Next(splittable.Length)];
            var depth = leaves[chosen] + 1;
            leaves[chosen] = depth;
            leaves.Add(depth);
        }

        return [.. leaves.OrderBy(_ => random.Next())];
    }

    private static ulong Reversed(uint code, int length)
    {
        ulong reversed = 0;
        for (var bit = 0; bit < length; bit++)
        {
            reversed |= (ulong)(code >> (length - 1 - bit) & 1) << bit;
        }

        return reversed;
    }
}
