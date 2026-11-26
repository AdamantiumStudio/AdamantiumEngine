namespace Adamantium.Pulsar;

/// <summary>A sound's waveform for drawing: of each channel, the least and the greatest sample of each of so many equal
/// parts of it. Read a part at a time, on a thread of its own: what shows it may draw the parts read so far, as
/// <see cref="BucketsRead"/> says, while the rest is read.</summary>
public sealed class SoundPeaks
{
    private const int ChunkFrames = 4096;

    private readonly float[] _minimums;
    private readonly float[] _maximums;
    private readonly long _frames;
    private readonly float[] _chunk;
    private long _read;
    private int _bucketsRead;

    /// <param name="frames">The sound's frames, as its decoder says; more than 0.</param>
    public SoundPeaks(int channels, int buckets, long frames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(buckets, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        Channels = channels;
        Buckets = buckets;
        _frames = frames;
        _minimums = new float[channels * buckets];
        _maximums = new float[channels * buckets];
        _minimums.AsSpan().Fill(float.PositiveInfinity);
        _maximums.AsSpan().Fill(float.NegativeInfinity);
        _chunk = new float[ChunkFrames * channels];
    }

    public int Channels { get; }

    public int Buckets { get; }

    /// <summary>The parts read: those before it are whole.</summary>
    public int BucketsRead => Volatile.Read(ref _bucketsRead);

    public bool IsComplete => BucketsRead == Buckets;

    /// <summary>Of <paramref name="decoder"/>'s sound, all its frames in the parts it has.</summary>
    public static SoundPeaks Of(ISoundDecoder decoder, int buckets)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return decoder.Frames > 0
            ? new SoundPeaks(decoder.Format.Channels, buckets, decoder.Frames)
            : throw new ArgumentException("A sound of a length not known has no peaks to read.", nameof(decoder));
    }

    /// <summary>0 of a part of no frames, or not read yet.</summary>
    public float MinimumOf(int channel, int bucket)
    {
        var minimum = _minimums[channel * Buckets + bucket];
        return float.IsPositiveInfinity(minimum) ? 0 : minimum;
    }

    /// <summary>0 of a part of no frames, or not read yet.</summary>
    public float MaximumOf(int channel, int bucket)
    {
        var maximum = _maximums[channel * Buckets + bucket];
        return float.IsNegativeInfinity(maximum) ? 0 : maximum;
    }

    /// <summary>Up to <paramref name="frames"/> more of <paramref name="decoder"/>, of as many channels, which reads on
    /// from where it was left; false once the sound is read.</summary>
    public bool ReadOn(ISoundDecoder decoder, int frames)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var channels = Channels;
        if (decoder.Format.Channels != channels)
        {
            throw new ArgumentException($"The peaks are of {channels} channels, the sound of {decoder.Format.Channels}.",
                nameof(decoder));
        }

        while (frames > 0 && _read < _frames)
        {
            var wanted = (int)Math.Min(Math.Min(frames, ChunkFrames), _frames - _read);
            var got = decoder.Read(_chunk.AsSpan(0, wanted * channels));
            if (got == 0)
            {
                // Shorter than it said: the rest is silence.
                Finish();
                return false;
            }

            for (var frame = 0; frame < got; frame++)
            {
                var bucket = (int)((_read + frame) * Buckets / _frames);
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = _chunk[frame * channels + channel];
                    var at = channel * Buckets + bucket;
                    _minimums[at] = Math.Min(_minimums[at], sample);
                    _maximums[at] = Math.Max(_maximums[at], sample);
                }
            }

            _read += got;
            frames -= got;
            Volatile.Write(ref _bucketsRead, _read == _frames ? Buckets : (int)(_read * Buckets / _frames));
        }

        return _read < _frames;
    }

    private void Finish()
    {
        _read = _frames;
        Volatile.Write(ref _bucketsRead, Buckets);
    }
}
