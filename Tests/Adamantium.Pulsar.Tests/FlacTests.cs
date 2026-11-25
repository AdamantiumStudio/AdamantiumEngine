using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>FLAC files of the reference encoder, against what was encoded. The whole of the test files of RFC 9639's authors
/// was decoded alike, and is too large to be here.</summary>
[TestFixture]
public class FlacTests
{
    [TestCase("stereo16", 2, 16, 10000, 4, 44100)]
    [TestCase("leftside16", 2, 16, 10000, 4, 44100)]
    [TestCase("small16", 2, 16, 10000, 4, 44100)]
    [TestCase("mono8", 1, 8, 3000, 4, 22050)]
    [TestCase("mono24", 1, 24, 6000, 4, 48000)]
    [TestCase("stereo32", 2, 32, 4000, 4, 48000)]
    [TestCase("noise16", 2, 16, 2000, 16000, 48000)]
    [TestCase("long16", 1, 16, 120000, 2, 48000)]
    public void WhatWasEncoded_IsDecoded(string name, int channels, int bits, int frames, int noise, int rate)
    {
        var sound = SoundFile.Read(FlacFiles.Read(name));

        Assert.That(sound.Format, Is.EqualTo(new AudioFormat(rate, channels)));
        Assert.That(sound.AsSpan().SequenceEqual(new FlacSignal(channels, bits, frames, noise).ToFloats()), Is.True);
    }

    [TestCase("long16")]
    [TestCase("longnotable16")]
    public void Sought_ItReadsOnFromThere(string name)
    {
        using var decoder = SoundFile.Open(new MemoryStream(FlacFiles.Read(name)));
        var random = new Random(1);
        var samples = new float[5000];
        int[] frames = [0, 1, 4095, 4096, 119999, 120000, 0, .. Enumerable.Range(0, 300).Select(_ => random.Next(FlacFiles.LongFrames))];
        foreach (var frame in frames)
        {
            decoder.Seek(frame);
            var got = decoder.Read(samples);

            Assert.That(got, Is.EqualTo(Math.Min(samples.Length, FlacFiles.LongFrames - frame)), $"frame {frame}");
            Assert.That(samples.AsSpan(0, got).SequenceEqual(FlacFiles.Long.AsSpan(frame, got)), Is.True, $"frame {frame}");
        }

        Assert.That(() => decoder.Seek(FlacFiles.LongFrames + 1), Throws.InstanceOf<InvalidDataException>());
        Assert.That(() => decoder.Seek(-1), Throws.InstanceOf<InvalidDataException>());
    }

    // The places in the table are the file's word.
    [Test]
    public void AWrongSeekTable_OnlySlowsItDown()
    {
        var file = FlacFiles.Read("long16");
        // The table's points are after its header: 4 and 38 bytes before, 4 its own.
        for (var at = 42 + 4 + 8; at < 42 + 4 + 36; at++)
        {
            file[at] = (byte)(at * 37);
        }

        using var decoder = SoundFile.Open(new MemoryStream(file));
        var samples = new float[100];
        foreach (var frame in new[] { 100000, 60000, 5 })
        {
            decoder.Seek(frame);
            Assert.That(decoder.Read(samples), Is.EqualTo(100));
            Assert.That(samples.AsSpan().SequenceEqual(FlacFiles.Long.AsSpan(frame, 100)), Is.True, $"frame {frame}");
        }
    }

    [Test]
    public void TheLoopOfItsComments_IsItsLoop()
    {
        Assert.That(SoundFile.Read(FlacFiles.Read("long16")).Loop, Is.EqualTo(new SoundLoop(20000, 70000)));
        Assert.That(SoundFile.Read(FlacFiles.Read("longnotable16")).Loop, Is.EqualTo(new SoundLoop(300, 120000)));
        Assert.That(SoundFile.Read(FlacFiles.Read("mono8")).Loop, Is.EqualTo(new SoundLoop(0, 3000)));
    }

    [Test]
    public void ADamagedFrame_IsRefused()
    {
        var file = FlacFiles.Read("mono24");
        file[^500] ^= 1;

        Assert.That(() => SoundFile.Read(file), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void AFileCutShort_IsRefused_WhereverItIsCut()
    {
        var file = FlacFiles.Read("mono8");
        for (var length = 0; length < file.Length; length++)
        {
            var cut = file[..length];
            Assert.That(() => SoundFile.Read(cut), Throws.InstanceOf<InvalidDataException>(), $"{length} bytes");
        }
    }

    [Test]
    public void ASpoiltFile_IsRefusedOrRead()
    {
        var file = FlacFiles.Read("mono8");
        for (var at = 0; at < file.Length; at++)
        {
            foreach (byte value in new byte[] { 0, 1, 0x7F, 0x80, 0xFF })
            {
                var spoilt = (byte[])file.Clone();
                spoilt[at] = value;
                try
                {
                    SoundFile.Read(spoilt);
                }
                catch (InvalidDataException)
                {
                }
            }
        }
    }

    [Test]
    public void Decoding_AllocatesNothing()
    {
        using var decoder = SoundFile.Open(new MemoryStream(FlacFiles.Read("long16")));
        var samples = new float[4096];
        decoder.Read(samples);
        var least = long.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            decoder.Seek(50000);
            decoder.Read(samples);
            decoder.Seek(0);
            decoder.Read(samples);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.That(least, Is.Zero);
    }
}
