using Adamantium.Mathematics;

namespace Adamantium.Pulsar.Sandbox;

/// <summary>A tone of 440 Hz flying past the listener at 20 m/s, 3 m in front, from the left to the right: it gets louder,
/// goes across, and drops in pitch as it passes.</summary>
internal static class FlyBy
{
    private const float Speed = 20;
    private const float Ahead = 3;

    public static void Play(int seconds)
    {
        var rate = AudioFormat.Default.SampleRate;
        var samples = new float[rate];
        for (var frame = 0; frame < samples.Length; frame++)
        {
            samples[frame] = 0.25f * MathF.Sin(MathF.Tau * 440 * frame / rate);
        }

        using var engine = new AudioEngine();
        using var output = AudioOutput.CreateDefault(TimeSpan.FromMilliseconds(80));
        var settings = new SpatialSettings { Place = PlaceAt(0, seconds) };
        var voice = engine.Play(engine.AddSound(new SoundData(new AudioFormat(rate, 1), samples)),
            new VoiceSettings { Loops = true, Spatial = settings });
        output.Start(engine);
        var started = DateTime.UtcNow;
        double elapsed;
        while ((elapsed = (DateTime.UtcNow - started).TotalSeconds) < seconds && output.State != AudioOutputState.Failed)
        {
            engine.SetPlace(voice, PlaceAt(elapsed, seconds));
            Thread.Sleep(10);
        }

        engine.Stop(voice, TimeSpan.FromMilliseconds(50));
        Thread.Sleep(output.Buffer + TimeSpan.FromMilliseconds(100));
        Console.WriteLine($"{output.State} {output.DeviceProblem} {output.Statistics}");
    }

    // Halfway through, right in front.
    private static VoicePlace PlaceAt(double elapsed, int seconds) => new()
    {
        Position = new Vector3<float>((float)((elapsed - seconds / 2.0) * Speed), 0, Ahead),
        Velocity = new Vector3<float>(Speed, 0, 0),
    };
}
