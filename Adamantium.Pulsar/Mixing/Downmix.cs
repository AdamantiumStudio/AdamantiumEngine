namespace Adamantium.Pulsar.Mixing;

/// <summary>Brings a sound to two channels by its speakers: ITU-R BS.775 - the center and the surrounds 3 dB down in their
/// sides, the low frequencies left out. Speakers it does not name go where the nearest it names does. Not scaled down:
/// a loud 5.1 sound goes past 1, as voices played together do, and the master's limiter is what keeps it in.</summary>
internal static class Downmix
{
    private const float Down3 = 0.70710677f;
    private const float Down6 = 0.5f;
    // Between the front left and the center: a pan of -0.5 keeping its power.
    private const float NearSide = 0.9238795f;
    private const float FarSide = 0.38268343f;

    /// <summary>The left and the right gain of each channel, in their order; null for one, which is panned, and for the
    /// front two, which are as they are.</summary>
    public static float[] GainsOf(AudioFormat format)
    {
        if (format.Channels == 1 || format.Speakers == AudioFormat.UsualFor(2))
        {
            return null;
        }

        var gains = new float[format.Channels * 2];
        var channel = 0;
        for (var bit = 1u; bit != 0 && channel < format.Channels; bit <<= 1)
        {
            if (((uint)format.Speakers & bit) != 0)
            {
                (gains[channel * 2], gains[channel * 2 + 1]) = GainsOf((Speakers)bit);
                channel++;
            }
        }

        return gains;
    }

    /// <summary>In place: the first <paramref name="frames"/> frames of two channels, from those of many.</summary>
    public static void Apply(Span<float> samples, int frames, int channels, float[] gains)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            var left = 0f;
            var right = 0f;
            var source = samples.Slice(frame * channels, channels);
            for (var channel = 0; channel < channels; channel++)
            {
                left += source[channel] * gains[channel * 2];
                right += source[channel] * gains[channel * 2 + 1];
            }

            // Not past the frame read.
            samples[frame * 2] = left;
            samples[frame * 2 + 1] = right;
        }
    }

    private static (float Left, float Right) GainsOf(Speakers speaker) => speaker switch
    {
        Speakers.FrontLeft or Speakers.TopFrontLeft => (1, 0),
        Speakers.FrontRight or Speakers.TopFrontRight => (0, 1),
        Speakers.FrontCenter or Speakers.TopFrontCenter => (Down3, Down3),
        Speakers.BackLeft or Speakers.SideLeft or Speakers.TopBackLeft => (Down3, 0),
        Speakers.BackRight or Speakers.SideRight or Speakers.TopBackRight => (0, Down3),
        Speakers.BackCenter or Speakers.TopBackCenter or Speakers.TopCenter => (Down6, Down6),
        Speakers.FrontLeftOfCenter => (NearSide, FarSide),
        Speakers.FrontRightOfCenter => (FarSide, NearSide),
        _ => (0, 0),
    };
}
