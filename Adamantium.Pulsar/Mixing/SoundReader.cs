namespace Adamantium.Pulsar.Mixing;

/// <summary>Reads a voice's sound at its pitch and the mixer's rate.</summary>
internal static class SoundReader
{
    /// <summary>A frame, as positions count.</summary>
    public const long One = 1L << 32;

    public const float FractionScale = 1f / One;

    /// <summary>How far the sound goes in a frame of the mixer.</summary>
    public static long StepOf(in Voice voice, int sampleRate) =>
        Math.Max(1, (long)((double)voice.Pitch * voice.Format.SampleRate / sampleRate * One));

    /// <summary>Goes on by <paramref name="frames"/> unread; false where the sound ended.</summary>
    public static bool Skip(ref Voice voice, long step, int frames)
    {
        voice.Position += step * frames;
        return Wrap(ref voice);
    }

    /// <summary>Fills <paramref name="samples"/> and returns how many frames there were, fewer where the sound ended.
    /// </summary>
    public static int Read(ref Voice voice, long step, Span<float> samples)
    {
        var channels = voice.Sound.Format.Channels;
        var frames = samples.Length / channels;
        var done = 0;
        while (done < frames && Wrap(ref voice))
        {
            var rest = samples[(done * channels)..];
            if (step == One && (uint)voice.Position == 0)
            {
                done += Copy(ref voice, rest, channels);
            }
            else
            {
                done += channels == 1 ? ReadMono(ref voice, step, rest) : ReadStereo(ref voice, step, rest);
            }
        }

        // Where it is told to be is within its loop.
        Wrap(ref voice);
        return done;
    }

    private static int EndOf(in Voice voice) => voice.Loops ? voice.Sound.Loop.End : voice.Sound.Frames;

    // False where it ended. A loop shorter than a step is gone round several times.
    private static bool Wrap(ref Voice voice)
    {
        var end = (long)EndOf(voice) << 32;
        if (voice.Position < end)
        {
            return true;
        }

        if (!voice.Loops)
        {
            return false;
        }

        var start = (long)voice.Sound.Loop.Start << 32;
        voice.Position = start + (voice.Position - start) % (end - start);
        voice.HasLooped = true;
        return true;
    }

    private static int Copy(ref Voice voice, Span<float> samples, int channels)
    {
        var at = (int)(voice.Position >> 32);
        var frames = Math.Min(samples.Length / channels, EndOf(voice) - at);
        voice.Sound.Samples.AsSpan(at * channels, frames * channels).CopyTo(samples);
        voice.Position += (long)frames << 32;
        return frames;
    }

    // Until the end of the sound or of its loop.
    private static int ReadMono(ref Voice voice, long step, Span<float> samples)
    {
        var source = voice.Sound.Samples;
        var start = voice.HasLooped ? voice.Sound.Loop.Start : 0;
        var end = EndOf(voice);
        var position = voice.Position;
        var frame = 0;
        for (; frame < samples.Length; frame++)
        {
            var at = (int)(position >> 32);
            if (at >= end)
            {
                break;
            }

            var fraction = (uint)position * FractionScale;
            samples[frame] = at > start && at + 2 < end
                ? Interpolate(source[at - 1], source[at], source[at + 1], source[at + 2], fraction)
                : InterpolateAtEdge(voice, at, 0, fraction);
            position += step;
        }

        voice.Position = position;
        return frame;
    }

    private static int ReadStereo(ref Voice voice, long step, Span<float> samples)
    {
        var source = voice.Sound.Samples;
        var start = voice.HasLooped ? voice.Sound.Loop.Start : 0;
        var end = EndOf(voice);
        var position = voice.Position;
        var frame = 0;
        for (; frame < samples.Length / 2; frame++)
        {
            var at = (int)(position >> 32);
            if (at >= end)
            {
                break;
            }

            var fraction = (uint)position * FractionScale;
            var inside = at > start && at + 2 < end;
            for (var channel = 0; channel < 2; channel++)
            {
                var sample = at * 2 + channel;
                samples[frame * 2 + channel] = inside
                    ? Interpolate(source[sample - 2], source[sample], source[sample + 2], source[sample + 4], fraction)
                    : InterpolateAtEdge(voice, at, channel, fraction);
            }

            position += step;
        }

        voice.Position = position;
        return frame;
    }

    private static float InterpolateAtEdge(in Voice voice, int frame, int channel, float fraction) =>
        Interpolate(SampleAt(voice, frame - 1, channel), SampleAt(voice, frame, channel), SampleAt(voice, frame + 1, channel),
            SampleAt(voice, frame + 2, channel), fraction);

    // Past an edge too: round the loop, or silence.
    private static float SampleAt(in Voice voice, int frame, int channel)
    {
        var loop = voice.Sound.Loop;
        if (voice.Loops && frame >= loop.End)
        {
            frame = loop.Start + (frame - loop.Start) % (loop.End - loop.Start);
        }
        else if (voice.HasLooped && frame < loop.Start)
        {
            frame = loop.End - 1;
        }

        return (uint)frame < (uint)voice.Sound.Frames
            ? voice.Sound.Samples[frame * voice.Sound.Format.Channels + channel]
            : 0;
    }

    /// <summary>Catmull-Rom.</summary>
    public static float Interpolate(float before, float from, float to, float after, float fraction)
    {
        var slope = 0.5f * (to - before);
        var square = before - 2.5f * from + 2 * to - 0.5f * after;
        var cube = 0.5f * (after - before) + 1.5f * (from - to);
        return ((cube * fraction + square) * fraction + slope) * fraction + from;
    }
}
