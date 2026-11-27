namespace Adamantium.Pulsar;

/// <summary>A sound's waveform for drawing: of each channel, the least and the greatest sample of each of so many equal
/// parts of it, and how loud the part is on average - what players draw. It is read in the background
/// (<see cref="AudioPlayback.ReadWaveform(int, CancellationToken)"/>): the parts before <see cref="PartsRead"/> may be
/// drawn while the rest is read.</summary>
public sealed class SoundWaveform
{
    private const int ChunkFrames = 4096;

    private readonly float[] _minimums;
    private readonly float[] _maximums;
    // Of each channel's part, the sum of its samples squared.
    private readonly double[] _squares;
    private readonly long _frames;
    private readonly float[] _chunk;
    private long _read;
    private int _partsRead;

    /// <param name="channels">The sound's channels.</param>
    /// <param name="parts">How many parts the waveform is cut into.</param>
    /// <param name="frames">The sound's frames, as its decoder says; more than 0.</param>
    internal SoundWaveform(int channels, int parts, long frames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        Channels = channels;
        Parts = parts;
        _frames = frames;
        _minimums = new float[channels * parts];
        _maximums = new float[channels * parts];
        _squares = new double[channels * parts];
        _minimums.AsSpan().Fill(float.PositiveInfinity);
        _maximums.AsSpan().Fill(float.NegativeInfinity);
        _chunk = new float[ChunkFrames * channels];
    }

    public int Channels { get; }

    public int Parts { get; }

    /// <summary>The parts read: those before it are whole.</summary>
    public int PartsRead => Volatile.Read(ref _partsRead);

    public bool IsComplete => PartsRead == Parts;

    /// <summary>0 of a part of no frames, or not read yet.</summary>
    public float MinimumOf(int channel, int part)
    {
        var minimum = _minimums[channel * Parts + part];
        return float.IsPositiveInfinity(minimum) ? 0 : minimum;
    }

    /// <summary>0 of a part of no frames, or not read yet.</summary>
    public float MaximumOf(int channel, int part)
    {
        var maximum = _maximums[channel * Parts + part];
        return float.IsNegativeInfinity(maximum) ? 0 : maximum;
    }

    /// <summary>How loud the part is on average - the root of its samples' mean square, from 0 to 1 for a sound within
    /// full scale; 0 of a part of no frames, or not wholly read yet. Of a sound shorter than it said, what was not read
    /// is silence in it.</summary>
    public float LoudnessOf(int channel, int part)
    {
        var frames = FirstFrameOf(part + 1) - FirstFrameOf(part);
        return part < PartsRead && frames > 0 ? (float)Math.Sqrt(_squares[channel * Parts + part] / frames) : 0;
    }

    // The first frame of the part: the part of frame f is f * Parts / frames, rounded down.
    private long FirstFrameOf(int part) => (long)(((Int128)part * _frames + Parts - 1) / Parts);

    /// <summary>Of <paramref name="decoder"/>'s sound, all its frames in the parts it has.</summary>
    internal static SoundWaveform Of(ISoundDecoder decoder, int parts)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return decoder.Frames > 0
            ? new SoundWaveform(decoder.Format.Channels, parts, decoder.Frames)
            : throw new ArgumentException("A sound of a length not known has no waveform to read.", nameof(decoder));
    }

    /// <summary>Up to <paramref name="frames"/> more of <paramref name="decoder"/>, of as many channels, which reads on
    /// from where it was left; false once the sound is read.</summary>
    internal bool ReadOn(ISoundDecoder decoder, int frames)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var channels = Channels;
        if (decoder.Format.Channels != channels)
        {
            throw new ArgumentException($"The waveform is of {channels} channels, the sound of {decoder.Format.Channels}.",
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
                var part = (int)((_read + frame) * Parts / _frames);
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = _chunk[frame * channels + channel];
                    var at = channel * Parts + part;
                    _minimums[at] = Math.Min(_minimums[at], sample);
                    _maximums[at] = Math.Max(_maximums[at], sample);
                    _squares[at] += (double)sample * sample;
                }
            }

            _read += got;
            frames -= got;
            Volatile.Write(ref _partsRead, _read == _frames ? Parts : (int)(_read * Parts / _frames));
        }

        return _read < _frames;
    }

    /// <summary>No more is read: what was not is silence.</summary>
    internal void Finish()
    {
        _read = _frames;
        Volatile.Write(ref _partsRead, Parts);
    }
}
