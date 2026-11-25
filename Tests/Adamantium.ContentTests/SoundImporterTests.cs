using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Content.Pipeline;
using Adamantium.Content.Pipeline.Importers;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;
using NUnit.Framework;

namespace Adamantium.ContentTests;

/// <summary>Sound files become asset files that hold them as they were.</summary>
[TestFixture]
public class SoundImporterTests
{
    private string _project;

    [SetUp]
    public void SetUp()
    {
        _project = Path.Combine(Path.GetTempPath(), "adamantium-sound-importer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_project);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_project, true);

    [Test]
    public void ASound_IsImported_AndReadBack()
    {
        var result = Import(("Boom.wav", Wav(1)));

        var asset = new AssetFileInfo(Path.Combine(_project, "Boom.wav.aemf"));
        var sound = Read("Boom.wav");
        Assert.Multiple(() =>
        {
            Assert.That((result.Cooked, result.Failed), Is.EqualTo((1, 0)));
            Assert.That((asset.Kind, asset.Name, asset.IsAssetFile), Is.EqualTo((AssetKind.Sound, "Boom.wav", true)));
            Assert.That(asset.Source.Importer, Is.EqualTo("Sound"));
            Assert.That(asset.ReadThumbnail(), Is.Null);
            Assert.That(sound, Is.InstanceOf<SoundData>());
            Assert.That(sound.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(((SoundData)sound).AsSpan()[100], Is.EqualTo(0.5f));
        });
    }

    [TestCase(SoundImporter.WhenLong, 10, false)]
    [TestCase(SoundImporter.WhenLong, 11, true)]
    [TestCase(SoundImporter.Always, 1, true)]
    [TestCase(SoundImporter.Never, 11, false)]
    public void ALongSound_IsStreamed_UnlessSaidOtherwise(string streaming, int seconds, bool streamed)
    {
        var builder = new ContentBuilder();
        builder.SetDefaultParameters("Sound", new Dictionary<string, string> { [SoundImporter.StreamingSetting] = streaming });

        Import(builder, ("Music.wav", Wav(seconds)));

        Assert.That(Read("Music.wav"), Is.InstanceOf(streamed ? typeof(StreamedSound) : typeof(SoundData)));
    }

    [Test]
    public void AStreamedSound_IsPlayedFromItsAssetFile()
    {
        Import(("Music.wav", Wav(11)));
        using var engine = new AudioEngine();
        engine.Play(engine.AddSound(Read("Music.wav")), new VoiceSettings { Pan = -1 });
        var samples = new float[512];

        Assert.That(
            System.Threading.SpinWait.SpinUntil(() =>
            {
                engine.Render(samples, AudioFormat.Default);
                return samples[^2] == 0.5f;
            }, TimeSpan.FromSeconds(20)),
            Is.True, engine.StreamProblem);
    }

    [Test]
    public void WhatIsNoSound_IsAnError()
    {
        var log = new List<string>();

        var result = Import(new ContentBuilder(), log.Add, ("Broken.wav", "RIFF but no more"u8.ToArray()));

        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(log.Any(line => line.Contains("error") && line.Contains("Broken.wav")), Is.True);
        Assert.That(File.Exists(Path.Combine(_project, "Broken.wav.aemf")), Is.False);
    }

    private Sound Read(string source) => BakedSound.Read(new Storage(Path.Combine(_project, source + ".aemf")));

    private BuildResult Import(params (string Name, byte[] Bytes)[] files) => Import(new ContentBuilder(), files);

    private BuildResult Import(ContentBuilder builder, params (string Name, byte[] Bytes)[] files) =>
        Import(builder, _ => { }, files);

    private BuildResult Import(ContentBuilder builder, Action<string> log, params (string Name, byte[] Bytes)[] files)
    {
        var manifest = new ContentManifest();
        foreach (var (name, bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(_project, name), bytes);
            manifest.Assets.Add(new ContentAsset { Source = name });
        }

        return builder.Import(manifest, _project, log);
    }

    // 8 kHz, one channel of 16 bits, every sample a half.
    private static byte[] Wav(int seconds)
    {
        var data = Enumerable.Repeat(new byte[] { 0, 0x40 }, seconds * 8000).SelectMany(sample => sample).ToArray();
        byte[] body =
        [
            .. Encoding.ASCII.GetBytes("WAVEfmt "), 16, 0, 0, 0, 1, 0, 1, 0, .. BitConverter.GetBytes(8000),
            .. BitConverter.GetBytes(16000), 2, 0, 16, 0, .. Encoding.ASCII.GetBytes("data"),
            .. BitConverter.GetBytes(data.Length), .. data
        ];
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(body.Length), .. body];
    }

    private sealed class Storage(string path) : ISoundStorage
    {
        public Stream Open() =>
            AssetFile.TryFindSection(path, AssetFileSection.Body, out var offset, out var length)
                ? new SoundFileStorage(path, offset, length).Open()
                : throw new InvalidDataException("no body");
    }
}
