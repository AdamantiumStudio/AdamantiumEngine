using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security.Cryptography;
using Adamantium.Imaging.Gif;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// A GIF frame is a delta over the one before it, so decoding walks the frames in order. The pixels are what every
// caller reads; the index streams are scaffolding, and only the newest one is needed to lay the next frame over.
[TestFixture]
public class GifFrameDecodeTests
{
    private static readonly string Assets = FindAssets();

    private static string FindAssets()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Tests", "TestAssets", "gif");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Tests/TestAssets/gif not found above " + AppContext.BaseDirectory);
    }

    private static GifImage Decode(string name)
    {
        using var file = MemoryMappedFile.CreateFromFile(Path.Combine(Assets, name), FileMode.Open, null, 0,
            MemoryMappedFileAccess.Read);
        using var view = file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
        return new GifDecoder().Decode(view);
    }

    // Hashes of every frame's pixels, taken from the decoder before it was made to stop allocating per code and per
    // frame: the same pixels, to the byte.
    [TestCase("infinity.gif", "3201C92DCCE374F0594CB955745B050076965777C75C5A380A0F43B7CD7F334F")]
    [TestCase("interlaced.gif", "9193619B132CEFCB1E4ED2564DCF96B449DEA0B59EC237859E92C9E3BC7A9CB4")]
    [TestCase("cube.gif", "BF61C4DD935FC9657CAF94497BD64FC29F52E7C39C4197CE2A6591CE3C5A865C")]
    [TestCase("bubble_animation.gif", "F1C2DDCFF775B5151DE396355EB68CC1D41A977FCC7A5CF5C76EA05346920654")]
    [TestCase("RotatingEarth2.gif", "2D12D470DB9FD9AE6F42EDAEAFE3E4AC9FF4C09135AD8EAAA00DFD93D7BFDA41")]
    public void EveryFrame_DecodesToTheSamePixels(string name, string expected)
    {
        var gif = Decode(name);

        using var sha = SHA256.Create();
        for (uint i = 0; i < gif.FramesCount; i++)
        {
            var pixels = gif.GetRawPixels(i);
            sha.TransformBlock(pixels, 0, pixels.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        Assert.That(Convert.ToHexString(sha.Hash), Is.EqualTo(expected));
    }

    // Copied out one by one, the frames are the same pixels and none of them stays behind in the decoder.
    [TestCase("infinity.gif", "3201C92DCCE374F0594CB955745B050076965777C75C5A380A0F43B7CD7F334F")]
    [TestCase("interlaced.gif", "9193619B132CEFCB1E4ED2564DCF96B449DEA0B59EC237859E92C9E3BC7A9CB4")]
    [TestCase("RotatingEarth2.gif", "2D12D470DB9FD9AE6F42EDAEAFE3E4AC9FF4C09135AD8EAAA00DFD93D7BFDA41")]
    public void FramesCopiedInOrder_AreTheSamePixels_AndNoneIsKept(string name, string expected)
    {
        var gif = Decode(name);
        var frame = new byte[((Adamantium.Imaging.IRawBitmap)gif).TotalSizeInBytes];

        using var sha = SHA256.Create();
        for (uint i = 0; i < gif.FramesCount; i++)
        {
            gif.CopyFrame(i, frame);
            sha.TransformBlock(frame, 0, frame.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexString(sha.Hash), Is.EqualTo(expected));
            Assert.That(gif.Frames.Count(f => f.RawPixels != null), Is.Zero, "no frame's pixels are kept");
        });
    }

    [Test]
    public void DecodingEveryFrame_LeavesNoIndexStreamOnTheFrames()
    {
        var gif = Decode("infinity.gif");

        for (uint i = 0; i < gif.FramesCount; i++) gif.GetRawPixels(i);

        Assert.That(gif.Frames.Count(f => f.IndexData != null), Is.Zero);
    }

    // Playback asks for early frames while the frames are being copied out in order: each keeps its own place, so
    // neither sends the other back to the first frame, and both get the same pixels as on their own.
    [Test]
    public void PlaybackBesideACopyInOrder_GetsTheSamePixels()
    {
        var alone = Decode("infinity.gif");
        var shared = Decode("infinity.gif");
        var frame = new byte[((Adamantium.Imaging.IRawBitmap)shared).TotalSizeInBytes];
        var expected = new byte[frame.Length];

        for (uint i = 0; i < shared.FramesCount; i++)
        {
            shared.CopyFrame(i, frame);
            alone.CopyFrame(i, expected);
            Assert.That(frame.AsSpan().SequenceEqual(expected), Is.True, $"copied frame {i}");

            var played = i / 2;
            Assert.That(shared.GetRawPixels(played).AsSpan().SequenceEqual(alone.GetRawPixels(played)), Is.True,
                $"played frame {played}");
        }
    }

    [Test]
    public void AFrameAskedForOutOfOrder_IsTheFrameDecodedInOrder()
    {
        var inOrder = Decode("infinity.gif");
        var skipped = Decode("infinity.gif");
        var last = inOrder.FramesCount - 1;

        for (uint i = 0; i <= last; i++) inOrder.GetRawPixels(i);
        var jumped = skipped.GetRawPixels(last);
        var middle = skipped.GetRawPixels(last / 2);

        Assert.Multiple(() =>
        {
            Assert.That(jumped.AsSpan().SequenceEqual(inOrder.GetRawPixels(last)), Is.True, "the last frame");
            Assert.That(middle.AsSpan().SequenceEqual(inOrder.GetRawPixels(last / 2)), Is.True, "the middle frame");
        });
    }
}
