using System.Text;
using Adamantium.Pulsar.Live;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>A station's stream with its metadata between the bytes of its sound, as ICY sends it.</summary>
[TestFixture]
public class IcyStreamTests
{
    // The sound comes whole, as it was before the texts were put in; each new title is told once.
    [Test]
    public void TheTexts_AreTakenOut_AndTheTitlesTold()
    {
        var sound = Enumerable.Range(0, 10_000).Select(index => (byte)(index * 31)).ToArray();
        string[] texts = ["StreamTitle='One - First';", "", "StreamTitle='One - First';", "StreamTitle='Two';StreamUrl='';"];
        var sent = Interleave(sound, 1000, texts);
        var titles = new List<string>();

        using var icy = new IcyStream(new ForwardOnlyStream(sent, mostAtOnce: 333), 1000, titles.Add);
        var read = new MemoryStream();
        icy.CopyTo(read);

        Assert.That(read.ToArray(), Is.EqualTo(sound));
        Assert.That(titles, Is.EqualTo(new[] { "One - First", "Two" }));
    }

    // Older stations write Latin-1; an apostrophe in the title is not its end.
    [Test]
    public void ATitle_IsUtf8_ElseLatin1()
    {
        Assert.That(IcyStream.TitleOf("StreamTitle='Björk - Jóga';"u8), Is.EqualTo("Björk - Jóga"));
        Assert.That(IcyStream.TitleOf(Encoding.Latin1.GetBytes("StreamTitle='Café del Mar';")), Is.EqualTo("Café del Mar"));
        Assert.That(IcyStream.TitleOf("StreamTitle='Don't Stop';"u8), Is.EqualTo("Don't Stop"));
        Assert.That(IcyStream.TitleOf("StreamUrl='x';"u8), Is.Null);
    }

    // Cut off in a text: the stream ends there, and nothing throws.
    [Test]
    public void CutOffInAText_ItEnds()
    {
        var sent = Enumerable.Repeat((byte)7, 100).Concat([(byte)4, (byte)'S', (byte)'t']).ToArray();

        using var icy = new IcyStream(new ForwardOnlyStream(sent), 100, null);
        var read = new MemoryStream();
        icy.CopyTo(read);

        Assert.That(read.Length, Is.EqualTo(100));
    }

    // The sound in runs of the interval, each followed by the next text - its length byte and the text padded to 16s -,
    // or by none once the texts run out.
    private static byte[] Interleave(byte[] sound, int interval, string[] texts)
    {
        var sent = new List<byte>();
        for (int at = 0, text = 0; at < sound.Length; at += interval, text++)
        {
            sent.AddRange(sound.AsSpan(at, Math.Min(interval, sound.Length - at)).ToArray());
            if (at + interval > sound.Length)
            {
                break;
            }

            var bytes = Encoding.UTF8.GetBytes(text < texts.Length ? texts[text] : "");
            var blocks = (bytes.Length + 15) / 16;
            sent.Add((byte)blocks);
            sent.AddRange(bytes);
            sent.AddRange(new byte[blocks * 16 - bytes.Length]);
        }

        return [.. sent];
    }
}
