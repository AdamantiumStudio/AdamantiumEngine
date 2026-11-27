using System.Buffers.Binary;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>Ogg as a station sends it: read only forward, a little at a time, link after link of its chain.</summary>
[TestFixture]
public class OggStreamTests
{
    // The samples the file gives, from the same place: the forward reader decodes as the file's does.
    [TestCase("ogg.stereo44.ogg")]
    [TestCase("ogg.mono22.ogg")]
    [TestCase("ogg.long8.ogg")]
    [TestCase("opus.music96.opus")]
    [TestCase("opus.hybrid24.opus")]
    public void AStream_IsTheFileAsItComes(string name)
    {
        var file = Read(name);
        var whole = SoundFile.Read(file);

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(file)), out var format);

        Assert.That(format, Is.EqualTo(whole.Format));
        var reference = whole.AsSpan();
        Assert.That(streamed.Length, Is.GreaterThanOrEqualTo(reference.Length));
        Assert.That(reference.ToArray().Max(Math.Abs), Is.GreaterThan(0.01f));
        for (var index = 0; index < reference.Length; index++)
        {
            Assert.That(streamed[index], Is.EqualTo(reference[index]).Within(1e-6f), $"sample {index}");
        }
    }

    // A station begins a link each song: of one format, they are one sound.
    [TestCase("ogg.stereo44.ogg")]
    [TestCase("opus.music96.opus")]
    public void LinksOfOneFormat_PlayOn(string name)
    {
        var file = Read(name);
        var frames = SoundFile.Read(file).Frames;

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream([.. file, .. file])), out var format);

        Assert.That(streamed.Length / format.Channels, Is.GreaterThanOrEqualTo(2 * frames));
    }

    // A link of another format ends what played: a voice plays one.
    [Test]
    public void ALinkOfAnotherFormat_EndsIt()
    {
        var first = Read("ogg.stereo44.ogg");
        var frames = SoundFile.Read(first).Frames;

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream([.. first, .. Read("ogg.mono22.ogg")])),
            out var format);

        Assert.That(streamed.Length / format.Channels, Is.InRange(frames, frames + 4096));
    }

    // A station is joined after its headers, at a page in the middle: what comes from there is heard.
    [TestCase("ogg.long8.ogg")]
    [TestCase("opus.long9.opus")]
    public void AStreamJoinedInTheMiddle_IsHeardFromThere(string name)
    {
        var file = Read(name);
        var pages = PagesOf(file);
        var firstAudio = pages.First(page => BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(page + 6)) > 0);
        var middle = pages[pages.Count / 2];
        var joined = file.AsSpan(0, firstAudio).ToArray().Concat(file.AsSpan(middle).ToArray()).ToArray();

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(joined)), out _);

        Assert.That(streamed.Length, Is.GreaterThan(0));
        Assert.That(streamed.Max(Math.Abs), Is.GreaterThan(0.01f));
    }

    // A grouped stream has its streams' first pages all first (RFC 3533): another's first page after the one played is
    // not the next link.
    [Test]
    public void AnotherStreamsFirstPage_InAGroup_IsNoNextLink()
    {
        var file = Read("ogg.stereo44.ogg");
        var other = Read("ogg.mono22.ogg");
        var otherFirst = other.AsSpan(0, OggPages.Of(other)[1]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(otherFirst.AsSpan(14), 0x5EED1234);
        OggPages.Seal(otherFirst, 0);
        var second = OggPages.Of(file)[1];
        byte[] grouped = [.. file.AsSpan(0, second), .. otherFirst, .. file.AsSpan(second)];

        var streamed = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(grouped)), out var format);

        Assert.That(streamed.Length / format.Channels, Is.GreaterThanOrEqualTo(SoundFile.Read(file).Frames));
    }

    // Icecast sends a joining listener the headers, then the page that plays now, numbered as it is: what is heard
    // begins where that page's packets do, nothing of them cut as a link's beginning is.
    [Test]
    public void AnOpusStreamJoinedInTheMiddle_CutsNothingOfIt()
    {
        // Of ffmpeg: three seconds of a sine at 32 kbit/s, a page each 40 ms.
        var file = Read("opus.paged32.opus");
        var pages = OggPages.Of(file);
        var firstAudio = pages.First(page => OggPages.GranuleOf(file, page) > 0);
        var index = pages.Count / 2;
        while ((file[pages[index] + 5] & 1) != 0)
        {
            index++;
        }

        Assert.That(pages[index], Is.GreaterThan(pages[pages.IndexOf(firstAudio) + 1]), "joined in the middle");

        var whole = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(file)), out var format);
        var joined = ReadAll(SoundFile.OpenForward(new ForwardOnlyStream(
            [.. file.AsSpan(0, firstAudio), .. file.AsSpan(pages[index])])), out _);

        // The whole begins after its pre-skip, at granule 312 of a stream begun at 0; the joined at the page before's.
        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(pages[0] + 28 + 10));
        var before = OggPages.GranuleOf(file, pages[index - 1]);
        Assert.That(joined.Length / format.Channels, Is.EqualTo(whole.Length / format.Channels - (before - preSkip)));
    }

    [Test]
    public void AnOggStream_IsNotSoughtIn()
    {
        using var decoder = SoundFile.OpenForward(new ForwardOnlyStream(Read("ogg.stereo44.ogg")));

        Assert.That(decoder.Frames, Is.EqualTo(-1));
        Assert.Throws<InvalidDataException>(() => decoder.Seek(100));
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

    // Where each page begins, by the lengths their headers say.
    private static List<int> PagesOf(byte[] file)
    {
        var pages = new List<int>();
        for (var at = 0; at + 27 <= file.Length && file.AsSpan(at).StartsWith("OggS"u8);)
        {
            pages.Add(at);
            var segments = file[at + 26];
            var body = file.AsSpan(at + 27, segments).ToArray().Sum(segment => segment);
            at += 27 + segments + body;
        }

        return pages;
    }

    private static byte[] Read(string name)
    {
        using var resource = typeof(OggStreamTests).Assembly.GetManifestResourceStream(name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
