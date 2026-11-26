using System.Diagnostics;

namespace Adamantium.Pulsar.Sandbox;

/// <summary>What mixing costs: the part of a core's time ten seconds of sound take.</summary>
internal static class MixingCost
{
    private const int Seconds = 10;

    public static void Measure()
    {
        Console.WriteLine("voices  heard  channels  pitch   part of a core");
        foreach (var voices in new[] { 0, 64, 256, 1024 })
        {
            foreach (var channels in new[] { 1, 2, 6 })
            {
                foreach (var pitch in new[] { 1f, 1.1f })
                {
                    Measure(voices, channels, pitch);
                }
            }
        }
    }

    private static void Measure(int voices, int channels, float pitch)
    {
        var engine = new AudioEngine();
        var samples = new float[AudioFormat.Default.SampleRate * channels];
        var random = new Random(1);
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = random.NextSingle() * 2 - 1;
        }

        var sound = engine.AddSound(new SoundData(new AudioFormat(AudioFormat.Default.SampleRate, channels), samples));
        for (var voice = 0; voice < voices; voice++)
        {
            engine.Play(sound, new VoiceSettings { Loops = true, Pitch = pitch, Volume = 0.1f + voice * 0.0001f });
        }

        var block = new float[480 * AudioFormat.Default.Channels];
        RenderFor(1);
        var started = Stopwatch.GetTimestamp();
        RenderFor(Seconds);
        var part = Stopwatch.GetElapsedTime(started) / TimeSpan.FromSeconds(Seconds);
        Console.WriteLine($"{voices,6} {engine.AudibleVoices,6} {channels,9} {pitch,6} {part,12:P2}");

        void RenderFor(int seconds)
        {
            for (var count = 0; count < seconds * 100; count++)
            {
                engine.Render(block, AudioFormat.Default);
            }
        }
    }
}
