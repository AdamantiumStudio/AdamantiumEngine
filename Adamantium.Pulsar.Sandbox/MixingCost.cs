using System.Diagnostics;
using Adamantium.Mathematics;

namespace Adamantium.Pulsar.Sandbox;

/// <summary>What mixing costs: the part of a core's time ten seconds of sound take. Voices in space go round the
/// listener, each moved every 10 ms, as a game would.</summary>
internal static class MixingCost
{
    private const int Seconds = 10;

    public static void Measure()
    {
        Console.WriteLine("voices  heard  channels  pitch   part of a core  in space");
        foreach (var voices in new[] { 0, 64, 256, 1024 })
        {
            foreach (var channels in new[] { 1, 2, 6 })
            {
                foreach (var pitch in new[] { 1f, 1.1f })
                {
                    Measure(voices, channels, pitch, false);
                }
            }

            Measure(voices, 1, 1, true);
        }
    }

    private static void Measure(int voices, int channels, float pitch, bool inSpace)
    {
        var engine = new AudioEngine();
        var samples = new float[AudioFormat.Default.SampleRate * channels];
        var random = new Random(1);
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = random.NextSingle() * 2 - 1;
        }

        var sound = engine.AddSound(new SoundData(new AudioFormat(AudioFormat.Default.SampleRate, channels), samples));
        var played = new VoiceId[voices];
        for (var voice = 0; voice < voices; voice++)
        {
            played[voice] = engine.Play(sound, new VoiceSettings
            {
                Loops = true, Pitch = pitch, Volume = 0.1f + voice * 0.0001f,
                Spatial = inSpace ? new SpatialSettings { Place = PlaceOf(voice, 0) } : null,
            });
        }

        var block = new float[480 * AudioFormat.Default.Channels];
        RenderFor(1);
        var started = Stopwatch.GetTimestamp();
        RenderFor(Seconds);
        var part = Stopwatch.GetElapsedTime(started) / TimeSpan.FromSeconds(Seconds);
        Console.WriteLine($"{voices,6} {engine.AudibleVoices,6} {channels,9} {pitch,6} {part,12:P2}  {(inSpace ? "yes" : "no")}");

        void RenderFor(int seconds)
        {
            for (var count = 0; count < seconds * 100; count++)
            {
                if (inSpace)
                {
                    for (var voice = 0; voice < voices; voice++)
                    {
                        engine.SetPlace(played[voice], PlaceOf(voice, count));
                    }
                }

                engine.Render(block, AudioFormat.Default);
            }
        }
    }

    // On a circle 2 to 22 m out, going round at 10 m/s.
    private static VoicePlace PlaceOf(int voice, int time)
    {
        var radius = 2 + voice % 20;
        var angle = voice + time * 0.01f * 10 / radius;
        return new VoicePlace
        {
            Position = new Vector3<float>(radius * MathF.Cos(angle), 0, radius * MathF.Sin(angle)),
            Velocity = new Vector3<float>(-10 * MathF.Sin(angle), 0, 10 * MathF.Cos(angle)),
        };
    }
}
