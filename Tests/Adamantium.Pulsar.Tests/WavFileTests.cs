using System.Text;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

[TestFixture]
public class WavFileTests
{
    private const int Pcm = 1;
    private const int Float = 3;

    [TestCase(8, new byte[] { 0, 128, 192 }, new[] { -1f, 0f, 0.5f })]
    [TestCase(16, new byte[] { 0x00, 0x80, 0, 0, 0x00, 0x40 }, new[] { -1f, 0f, 0.5f })]
    [TestCase(24, new byte[] { 0, 0, 0x80, 0, 0, 0, 0, 0, 0x40 }, new[] { -1f, 0f, 0.5f })]
    [TestCase(32, new byte[] { 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0x40 }, new[] { -1f, 0f, 0.5f })]
    public void WholeSamples_AreRead(int bits, byte[] data, float[] expected)
    {
        var sound = WavFile.Read(Wav(Chunk("fmt ", Format(Pcm, 1, 44100, bits)), Chunk("data", data)));

        Assert.That(sound.AsSpan().ToArray(), Is.EqualTo(expected));
        Assert.That(sound.Format, Is.EqualTo(new AudioFormat(44100, 1)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Floats_AreRead_AndWhatIsNoNumberIsSilence(bool wide)
    {
        float[] samples = [0.25f, -0.5f, float.NaN, 1f];
        var data = wide
            ? samples.SelectMany(sample => BitConverter.GetBytes((double)sample)).ToArray()
            : samples.SelectMany(BitConverter.GetBytes).ToArray();

        var sound = WavFile.Read(Wav(Chunk("fmt ", Format(Float, 2, 48000, wide ? 64 : 32)), Chunk("data", data)));

        Assert.That(sound.AsSpan().ToArray(), Is.EqualTo(new[] { 0.25f, -0.5f, 0f, 1f }));
        Assert.That(sound.Frames, Is.EqualTo(2));
    }

    [Test]
    public void AnExtensibleFormat_IsReadByWhatItStandsFor()
    {
        byte[] format = [.. Format(0xFFFE, 1, 48000, 16), 22, 0, 16, 0, 0, 0, 0, 0, Pcm, 0, .. new byte[14]];

        Assert.That(WavFile.Read(Wav(Chunk("fmt ", format), Chunk("data", [0, 0x40]))).AsSpan()[0], Is.EqualTo(0.5f));
    }

    // 24 bits in 32, the upper ones.
    [Test]
    public void SamplesInWiderContainers_AreReadByTheContainer()
    {
        var format = Format(Pcm, 1, 48000, 24);
        format[12] = 4;

        var sound = WavFile.Read(Wav(Chunk("fmt ", format), Chunk("data", [0, 0, 0, 0x40, 0, 0, 0, 0xC0])));

        Assert.That(sound.AsSpan().ToArray(), Is.EqualTo(new[] { 0.5f, -0.5f }));
    }

    [Test]
    public void OtherChunks_AreSkipped_OddOnesWithTheirPadding()
    {
        var file = Wav(Chunk("LIST", [1, 2, 3]), Chunk("fmt ", Format(Pcm, 1, 48000, 16)), Chunk("fact", [1]), Chunk("data", [0, 0x40]));

        Assert.That(WavFile.Read(file).AsSpan()[0], Is.EqualTo(0.5f));
    }

    [Test]
    public void ItsLoop_IsRead_TheLastFramePlayedIncluded()
    {
        var sound = WavFile.Read(Wav(Chunk("fmt ", Format(Pcm, 1, 48000, 8)), Chunk("data", new byte[100]), Chunk("smpl", Loop(10, 49))));

        Assert.That(sound.Loop, Is.EqualTo(new SoundLoop(10, 50)));
    }

    [TestCase(50u, 100u)]
    [TestCase(50u, 20u)]
    [TestCase(uint.MaxValue, uint.MaxValue)]
    public void ALoopOutsideIt_IsTheWholeSound(uint start, uint end)
    {
        var sound = WavFile.Read(Wav(Chunk("fmt ", Format(Pcm, 1, 48000, 8)), Chunk("data", new byte[100]), Chunk("smpl", Loop(start, end))));

        Assert.That(sound.Loop, Is.EqualTo(new SoundLoop(0, 100)));
    }

    [TestCase(uint.MaxValue)]
    [TestCase(1000u)]
    public void DataThatSaysItIsLongerThanTheFile_EndsWithIt(uint size)
    {
        byte[] data = [.. Encoding.ASCII.GetBytes("data"), .. BitConverter.GetBytes(size), 0, 0x40, 0];

        Assert.That(WavFile.Read(Wav(Chunk("fmt ", Format(Pcm, 1, 48000, 16)), data)).Frames, Is.EqualTo(1));
    }

    [Test]
    public void WhatIsNoWav_IsRefused()
    {
        var format = Chunk("fmt ", Format(Pcm, 1, 48000, 16));
        byte[][] files =
        [
            [],
            Encoding.ASCII.GetBytes("OggS and so on and on"),
            Wav(),
            Wav(format),
            Wav(format, Chunk("data", [1])),
            Wav(Chunk("data", [1, 2])),
            Wav(Chunk("fmt ", [1, 0]), Chunk("data", [1, 2])),
            Wav(Chunk("fmt ", Format(Pcm, 6, 48000, 16)), Chunk("data", new byte[12])),
            Wav(Chunk("fmt ", Format(Pcm, 1, 0, 16)), Chunk("data", [1, 2])),
            Wav(Chunk("fmt ", Format(Pcm, 1, 48000, 12)), Chunk("data", [1, 2])),
            Wav(Chunk("fmt ", Format(2, 1, 48000, 4)), Chunk("data", [1, 2])),
        ];

        foreach (var file in files)
        {
            Assert.That(() => WavFile.Read(new MemoryStream(file)), Throws.InstanceOf<InvalidDataException>());
        }
    }

    // Every length of a good file, and any byte of it spoilt: refused or read, never anything else.
    [Test]
    public void ASpoiltFile_IsRefusedOrRead()
    {
        var file = Wav(Chunk("fmt ", Format(Pcm, 2, 48000, 16)), Chunk("data", new byte[40]), Chunk("smpl", Loop(1, 5)));
        for (var length = 0; length < file.Length; length++)
        {
            ReadOrRefuse(file[..length]);
        }

        for (var at = 0; at < file.Length; at++)
        {
            foreach (byte value in new byte[] { 0, 1, 0x7F, 0x80, 0xFF })
            {
                var spoilt = (byte[])file.Clone();
                spoilt[at] = value;
                ReadOrRefuse(spoilt);
            }
        }

        void ReadOrRefuse(byte[] bytes)
        {
            try
            {
                WavFile.Read(bytes.AsSpan());
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    private static byte[] Wav(params byte[][] chunks)
    {
        byte[] body = [.. Encoding.ASCII.GetBytes("WAVE"), .. chunks.SelectMany(chunk => chunk)];
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(body.Length), .. body];
    }

    private static byte[] Chunk(string name, byte[] data) =>
        [.. Encoding.ASCII.GetBytes(name), .. BitConverter.GetBytes(data.Length), .. data, .. new byte[data.Length & 1]];

    private static byte[] Format(int tag, int channels, int rate, int bits)
    {
        var frame = channels * bits / 8;
        return
        [
            .. BitConverter.GetBytes((ushort)tag), .. BitConverter.GetBytes((ushort)channels), .. BitConverter.GetBytes(rate),
            .. BitConverter.GetBytes(rate * frame), .. BitConverter.GetBytes((ushort)frame), .. BitConverter.GetBytes((ushort)bits),
        ];
    }

    private static byte[] Loop(uint start, uint end) =>
    [
        .. new byte[28], 1, 0, 0, 0, .. new byte[12], .. BitConverter.GetBytes(start), .. BitConverter.GetBytes(end), .. new byte[8],
    ];
}
