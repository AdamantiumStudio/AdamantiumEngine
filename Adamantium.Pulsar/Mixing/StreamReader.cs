namespace Adamantium.Pulsar.Mixing;

/// <summary>Reads a voice's stream at its pitch and the mixer's rate. A voice's <see cref="Voice.Position"/> is within a
/// frame: the frames before are the stream's <see cref="SoundStream.Read"/>.</summary>
internal static class StreamReader
{
    private const int Mask = SoundStream.Capacity - 1;

    /// <summary>Goes on by <paramref name="frames"/> unread, or to where the stream was read to; false where the sound
    /// ended.</summary>
    public static bool Skip(ref Voice voice, long step, int frames)
    {
        var hasAll = voice.Stream.HasAll;
        var left = voice.Stream.Written - voice.Stream.Read;
        voice.Position += step * frames;
        if (voice.Position >> 32 >= left)
        {
            voice.Position = left << 32;
            Advance(ref voice);
            return !hasAll;
        }

        Advance(ref voice);
        return true;
    }

    /// <summary>Fills <paramref name="samples"/> and returns how many frames there were, fewer where the sound ended.
    /// Where the stream was not read so far, the rest is silence, and <paramref name="starved"/>.</summary>
    public static int Read(ref Voice voice, long step, Span<float> samples, out bool starved)
    {
        // In this order: what was written before it was said to be all, is all.
        var hasAll = voice.Stream.HasAll;
        var left = voice.Stream.Written - voice.Stream.Read;
        var channels = voice.Stream.Format.Channels;
        var frames = samples.Length / channels;
        // The frames around the last are read with it: silence past the end.
        var readable = hasAll ? left : left - 2;
        var done = channels == 1
            ? ReadMono(ref voice, step, samples, left, readable)
            : ReadStereo(ref voice, step, samples, left, readable);
        Advance(ref voice);
        starved = done < frames && !hasAll;
        if (!starved)
        {
            return done;
        }

        samples[(done * channels)..].Clear();
        return frames;
    }

    private static int ReadMono(ref Voice voice, long step, Span<float> samples, long left, long readable)
    {
        var source = voice.Stream.Samples;
        var read = voice.Stream.Read;
        var position = voice.Position;
        var frame = 0;
        for (; frame < samples.Length; frame++)
        {
            var at = position >> 32;
            if (at >= readable)
            {
                break;
            }

            samples[frame] = step == SoundReader.One &&(uint)position == 0
                ? source[(int)(read + at) & Mask]
                : Interpolate(source, read, at, left, 1, 0, position);
            position += step;
        }

        voice.Position = position;
        return frame;
    }

    private static int ReadStereo(ref Voice voice, long step, Span<float> samples, long left, long readable)
    {
        var source = voice.Stream.Samples;
        var read = voice.Stream.Read;
        var position = voice.Position;
        var frame = 0;
        for (; frame < samples.Length / 2; frame++)
        {
            var at = position >> 32;
            if (at >= readable)
            {
                break;
            }

            if (step == SoundReader.One &&(uint)position == 0)
            {
                var sample = ((int)(read + at) & Mask) * 2;
                samples[frame * 2] = source[sample];
                samples[frame * 2 + 1] = source[sample + 1];
            }
            else
            {
                samples[frame * 2] = Interpolate(source, read, at, left, 2, 0, position);
                samples[frame * 2 + 1] = Interpolate(source, read, at, left, 2, 1, position);
            }

            position += step;
        }

        voice.Position = position;
        return frame;
    }

    private static float Interpolate(float[] source, long read, long at, long left, int channels, int channel, long position) =>
        SoundReader.Interpolate(
            SampleAt(source, read, at - 1, left, channels, channel), SampleAt(source, read, at, left, channels, channel),
            SampleAt(source, read, at + 1, left, channels, channel), SampleAt(source, read, at + 2, left, channels, channel),
            (uint)position * SoundReader.FractionScale);

    private static float SampleAt(float[] source, long read, long at, long left, int channels, int channel) =>
        read + at < 0 || at >= left ? 0 : source[((int)(read + at) & Mask) * channels + channel];

    // At a high pitch a step may be past what there is.
    private static void Advance(ref Voice voice)
    {
        var stream = voice.Stream;
        var whole = Math.Min(voice.Position >> 32, stream.Written - stream.Read);
        stream.Read += whole;
        voice.Position -= whole << 32;
    }
}
