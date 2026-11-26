namespace Adamantium.Pulsar.Decoding;

/// <summary>Samples decoded from an Ogg stream and not yet read, with where in the stream the first of them is once a
/// granule has said: what a decoder fills packet by packet, and a read takes from, passing by those before the one
/// wanted and giving silence for those a loss left out.</summary>
internal sealed class DecodedSamples(int channels, int frames)
{
    private float[] _samples = new float[frames * channels];
    private int _from;

    /// <summary>The frames held.</summary>
    public int Count { get; private set; }

    /// <summary>Of the stream, where the first held is, once <see cref="IsPlaced"/>.</summary>
    public long At { get; private set; }

    public bool IsPlaced { get; private set; }

    /// <summary>Room for <paramref name="count"/> frames after those held, to be written and then added by
    /// <see cref="Add"/>. Waiting for a granule to place them, more than a packet's are held.</summary>
    public Span<float> Reserve(int count)
    {
        var needed = (_from + Count + count) * channels;
        if (needed > _samples.Length)
        {
            if ((Count + count) * channels <= _samples.Length)
            {
                _samples.AsSpan(_from * channels, Count * channels).CopyTo(_samples);
                _from = 0;
            }
            else
            {
                Array.Resize(ref _samples, Math.Max(_samples.Length * 2, needed));
            }
        }

        return _samples.AsSpan((_from + Count) * channels, count * channels);
    }

    public void Add(int count) => Count += count;

    /// <summary>The first held is at <paramref name="at"/>.</summary>
    public void Place(long at)
    {
        At = at;
        IsPlaced = true;
    }

    public void Clear()
    {
        _from = Count = 0;
        IsPlaced = false;
    }

    /// <summary>Into <paramref name="samples"/>, up to <paramref name="wanted"/> frames from
    /// <paramref name="position"/>, which it moves on; those passed by are dropped. How many were written.</summary>
    public int Take(Span<float> samples, ref long position, int wanted)
    {
        // After a loss, until those held: silence.
        var lost = (int)Math.Min(wanted, Math.Max(0, At - position));
        if (lost > 0)
        {
            samples[..(lost * channels)].Clear();
            position += lost;
            return lost;
        }

        var before = (int)Math.Min(Count, Math.Max(0, position - At));
        Drop(before);
        var taken = Math.Min(wanted, Count);
        _samples.AsSpan(_from * channels, taken * channels).CopyTo(samples);
        Drop(taken);
        position += taken;
        return taken;
    }

    private void Drop(int count)
    {
        _from += count;
        Count -= count;
        At += count;
    }
}
