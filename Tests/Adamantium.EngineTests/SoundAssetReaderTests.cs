using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.ECS.Audio;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A sound is loaded as it was imported; a file that was not, by its length.</summary>
[TestFixture]
public class SoundAssetReaderTests
{
    private string _folder;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("adamantium-sounds-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, true);

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnAssetFile_IsReadAsItWasImported(bool streamed)
    {
        var path = Path.Combine(_folder, "Boom.wav.aemf");
        using (var body = new MemoryStream())
        {
            BakedSound.Write(body, Wav(100), streamed);
            using var stream = File.Create(path);
            // What follows the body is not read as its samples.
            new AssetFile { Body = body.ToArray(), Edits = new byte[64] }.WriteTo(stream);
        }

        var sound = await Read(path);

        Assert.That(sound, Is.InstanceOf(streamed ? typeof(StreamedSound) : typeof(SoundData)));
        Assert.That(sound.Frames, Is.EqualTo(100));
    }

    [TestCase(8000, typeof(SoundData))]
    [TestCase(88000, typeof(StreamedSound))]
    public async Task ASoundFile_IsStreamedWhenLong(int frames, Type expected)
    {
        var path = Path.Combine(_folder, "Boom.wav");
        File.WriteAllBytes(path, Wav(frames));

        Assert.That(await Read(path), Is.InstanceOf(expected));
        Assert.That(await Read(null, Wav(frames)), Is.InstanceOf(expected), "an embedded one");
    }

    [Test]
    public void AnAssetFileOfSomethingElse_IsRefused()
    {
        var path = Path.Combine(_folder, "Boom.wav.aemf");
        using (var stream = File.Create(path))
        {
            new AssetFile { Body = new byte[100] }.WriteTo(stream);
        }

        Assert.That(() => Read(path), Throws.InstanceOf<InvalidDataException>());
    }

    private static async Task<Sound> Read(string path, byte[] data = null) =>
        (Sound)await new SoundAssetReader().ReadAssetAsync(null,
            new AssetReaderParameters { AssetName = "Boom.wav", AssetPath = path, Data = data, AssetType = typeof(Sound) });

    // 8 kHz, one channel of 8 bits.
    private static byte[] Wav(int frames) =>
    [
        .. "RIFF"u8, .. BitConverter.GetBytes(36 + frames), .. "WAVEfmt "u8, 16, 0, 0, 0, 1, 0, 1, 0, 0x40, 0x1F, 0, 0,
        0x40, 0x1F, 0, 0, 1, 0, 8, 0, .. "data"u8, .. BitConverter.GetBytes(frames), .. new byte[frames]
    ];
}
