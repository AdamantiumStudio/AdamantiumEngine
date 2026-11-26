using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>ID3v2 tags as taggers write them: frames of flags, unsynchronised, of versions not known.</summary>
[TestFixture]
public class Id3TagsTests
{
    // Of version 4, the frames' sizes count their bytes unsynchronised: taken out of each frame, not of the whole.
    [Test]
    public void AnUnsynchronisedTagOfVersion4_IsReadFrameByFrame()
    {
        var tags = TagsOf(4, 0x80, Frame4("PRIV", 0, [0xFF, 0x00, 0x01, 0x02]), Frame4("TIT2", 0, [3, (byte)'A', (byte)'b']));

        Assert.That(tags.Title, Is.EqualTo("Ab"));
    }

    [Test]
    public void AFrameUnsynchronisedOfItself_IsRead()
    {
        var tags = TagsOf(4, 0, Frame4("TIT2", 0x02, [0, (byte)'A', 0xFF, 0x00, (byte)'b']));

        Assert.That(tags.Title, Is.EqualTo("Aÿb"));
    }

    // Its length after unsynchronisation is taken out, before the text.
    [Test]
    public void AFrameSayingItsLength_IsRead()
    {
        var tags = TagsOf(4, 0, Frame4("TIT2", 0x01, [0, 0, 0, 3, 3, (byte)'A', (byte)'b']));

        Assert.That(tags.Title, Is.EqualTo("Ab"));
    }

    [Test]
    public void AFrameOfAGroup_IsRead()
    {
        Assert.That(TagsOf(4, 0, Frame4("TIT2", 0x40, [7, 3, (byte)'A', (byte)'b'])).Title, Is.EqualTo("Ab"));
        Assert.That(TagsOf(3, 0, Frame3("TIT2", 0x20, [7, 3, (byte)'A', (byte)'b'])).Title, Is.EqualTo("Ab"));
    }

    // Compressed by zlib, which a player's tags are not worth.
    [Test]
    public void ACompressedFrame_IsPassedBy()
    {
        var compressed = Frame3("TIT2", 0x80, [0x41, 0x42, 0x43, 0x44, 0x78, 0x9C]);

        Assert.That(TagsOf(3, 0, compressed, Frame3("TPE1", 0, [3, (byte)'C'])), Is.EqualTo(new SoundTags(null, "C", null)));
    }

    [Test]
    public void ATagOfAVersionNotKnown_IsPassedBy()
    {
        Assert.That(TagsOf(5, 0, Frame4("TIT2", 0, [3, (byte)'A'])).IsEmpty, Is.True);
    }

    // Of version 2.2, that flag says the whole tag is compressed.
    [Test]
    public void ACompressedTagOfVersion2_IsPassedBy()
    {
        Assert.That(TagsOf(2, 0x40, [.. "TT2"u8, 0, 0, 2, 3, (byte)'A']).IsEmpty, Is.True);
    }

    private static byte[] Frame4(string id, byte format, byte[] content) =>
        [.. System.Text.Encoding.ASCII.GetBytes(id), .. SyncSafe(content.Length), 0, format, .. content];

    private static byte[] Frame3(string id, byte format, byte[] content) =>
        [.. System.Text.Encoding.ASCII.GetBytes(id), 0, 0, 0, (byte)content.Length, 0, format, .. content];

    private static byte[] SyncSafe(int value) =>
        [(byte)(value >> 21 & 0x7F), (byte)(value >> 14 & 0x7F), (byte)(value >> 7 & 0x7F), (byte)(value & 0x7F)];

    // The tag, before the frames of an MP3 file.
    private static SoundTags TagsOf(byte version, byte flags, params byte[][] frames)
    {
        byte[] body = [.. frames.SelectMany(frame => frame)];
        var file = PlayerSupportTests.Read("tagged24.mp3");
        var start = 10 + (file[6] << 21 | file[7] << 14 | file[8] << 7 | file[9]);
        byte[] tagged = [.. "ID3"u8, version, 0, flags, .. SyncSafe(body.Length), .. body, .. file.AsSpan(start)];
        using var decoder = SoundFile.Open(new MemoryStream(tagged));
        return decoder.Tags;
    }
}
