using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>MP3 as a station sends it: read only forward, a little at a time, its length not known.</summary>
[TestFixture]
public class Mp3StreamTests
{
    // How far the stream's sound may be behind the file's: the coder's delay, and a frame of no sound that says of the
    // others, which a stream decodes as silence.
    private const int LongestOffset = 4096;

    // The same samples as the file gives, behind it by the delay the file's tag says and the stream does not drop; of
    // two channels, one of a file of one in both.
    [TestCase("cbr128")]
    [TestCase("vbr48")]
    [TestCase("mono32")]
    [TestCase("lower22")]
    [TestCase("shine44")]
    public void AStream_IsTheFileAsItComes(string name)
    {
        var file = Read(name + ".mp3");
        var whole = SoundFile.Read(file);
        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(file)), out var format);

        Assert.That(format, Is.EqualTo(new AudioFormat(whole.Format.SampleRate, 2)));
        var reference = whole.AsSpan();
        var channels = whole.Format.Channels;
        var offset = OffsetOf(streamed, reference, channels);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0), "the file's samples are not in the stream's");
        // Matched on sound, not on silence.
        var compared = reference[..(Math.Min(reference.Length / channels, 44100) * channels)].ToArray();
        Assert.That(compared.Max(Math.Abs), Is.GreaterThan(0.05f));
        Assert.That(streamed.Length / 2, Is.GreaterThanOrEqualTo(offset + whole.Frames));
    }

    // A station is joined in the middle of a frame: the bytes before the next frame are passed by.
    [Test]
    public void AStreamJoinedMidFrame_BeginsAtTheNextFrame()
    {
        var file = Read("cbr128.mp3");
        var joined = file.AsSpan(file.Length / 3 + 7).ToArray();

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(joined)), out var format);

        Assert.That(format.SampleRate, Is.EqualTo(44100));
        Assert.That(streamed.Length / 2, Is.GreaterThan(0));
    }

    // Bytes that are no frame between the frames: passed by, and the frames after them are heard.
    [Test]
    public void DamageBetweenFrames_IsPassedBy()
    {
        var file = Read("cbr128.mp3");
        var whole = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(file)), out _);
        var middle = file.Length / 2;
        var damaged = file.AsSpan(0, middle).ToArray()
            .Concat(Enumerable.Range(0, 3000).Select(index => (byte)(index * 37)))
            .Concat(file.AsSpan(middle).ToArray())
            .ToArray();

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(damaged)), out _);

        // At most the two frames the damage cut, and what their loss leaves the next without.
        Assert.That(streamed.Length / 2, Is.EqualTo(whole.Length / 2).Within(3 * 1152));
    }

    [Test]
    public void BytesOfNoSound_AreNoStream()
    {
        var noise = Enumerable.Range(0, 200_000).Select(index => (byte)(index * 7919 >> 3)).ToArray();

        Assert.Throws<InvalidDataException>(() => SoundFile.OpenForward(new ForwardOnlyStream(noise)));
    }

    [Test]
    public void AStream_IsNotSoughtIn()
    {
        using var decoder = SoundFile.OpenForward(new ForwardOnlyStream(Read("cbr128.mp3")));

        Assert.That(decoder.Frames, Is.EqualTo(-1));
        Assert.Throws<InvalidDataException>(() => decoder.Seek(1000));
    }

    private static float[] ReadAll(ISoundDecoder decoder, out AudioFormat format)
    {
        using (decoder)
        {
            format = decoder.Format;
            var samples = new List<float>();
            var chunk = new float[1000 * format.Channels];
            int got;
            while ((got = decoder.Read(chunk)) > 0)
            {
                samples.AddRange(chunk.AsSpan(0, got * format.Channels));
            }

            return [.. samples];
        }
    }

    // Where in the stream's two channels the file's samples begin, the first channel of each matched over a second of
    // sound; -1 where nowhere near the beginning.
    private static int OffsetOf(float[] streamed, ReadOnlySpan<float> reference, int channels)
    {
        var compared = Math.Min(reference.Length / channels, 44100);
        for (var offset = 0; offset <= LongestOffset; offset++)
        {
            if ((offset + compared) * 2 > streamed.Length)
            {
                break;
            }

            var matches = true;
            for (var frame = 0; frame < compared && matches; frame++)
            {
                matches = Math.Abs(streamed[(offset + frame) * 2] - reference[frame * channels]) < 1e-5f;
            }

            if (matches)
            {
                return offset;
            }
        }

        return -1;
    }

    private static byte[] Read(string name)
    {
        using var resource = typeof(Mp3StreamTests).Assembly.GetManifestResourceStream("mp3." + name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
