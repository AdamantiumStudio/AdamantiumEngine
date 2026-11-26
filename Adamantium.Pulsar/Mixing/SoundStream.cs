namespace Adamantium.Pulsar.Mixing;

/// <summary>The sound one voice plays, read ahead of it: filled on the streamer's thread, read on the output's.</summary>
internal sealed class SoundStream(StreamedSound sound, int from, bool loops)
{
    /// <summary>A power of two: 1.4 s at 48 kHz.</summary>
    public const int Capacity = 1 << 16;

    private const int FillFrames = 4096;
    // A frame before the one read, and two after, are read with it.
    private const int Margin = 4;
    private const int EnoughToBegin = FillFrames * 2;

    private ISoundDecoder _decoder;
    // Of the sound, the frame decoded next.
    private long _next;
    private long _written;
    private long _read;
    private bool _hasAll;
    private bool _isReleased;

    public AudioFormat Format { get; } = sound.Format;

    /// <summary>The frames, each at its number's lowest bits.</summary>
    public float[] Samples { get; private set; }

    /// <summary>How many frames there are, from the beginning of the voice: those before <see cref="Read"/> are gone.
    /// </summary>
    public long Written => Volatile.Read(ref _written);

    /// <summary>The frame the voice got to.</summary>
    public long Read
    {
        get => Volatile.Read(ref _read);
        set => Volatile.Write(ref _read, value);
    }

    /// <summary>No more frames come.</summary>
    public bool HasAll => Volatile.Read(ref _hasAll);

    public bool IsReady => HasAll || Written >= EnoughToBegin;

    /// <summary>The voice is done with it.</summary>
    public bool IsReleased => Volatile.Read(ref _isReleased);

    public bool WantsMore => !HasAll && Room >= Capacity / 4;

    /// <summary>The frame of the sound the voice got to.</summary>
    public int SoundFrame
    {
        get
        {
            var frame = from + Read;
            if (loops && frame >= sound.Loop.End)
            {
                frame = sound.Loop.Start + (frame - sound.Loop.Start) % (sound.Loop.End - sound.Loop.Start);
            }

            return (int)frame;
        }
    }

    private long Room => Capacity - Margin - (Written - Read);

    public void Release() => Volatile.Write(ref _isReleased, true);

    /// <summary>Adds some frames; false where there is no room for them or no more come.</summary>
    public bool Fill(float[] samples)
    {
        Samples ??= samples;
        if (HasAll || Room < FillFrames)
        {
            return false;
        }

        if (_decoder == null)
        {
            Begin();
        }

        var end = loops ? sound.Loop.End : sound.Frames;
        // Not past the end of the samples: they begin again there.
        var at = (int)(_written & Capacity - 1);
        var wanted = (int)Math.Min(Math.Min(FillFrames, end - _next), Capacity - at);
        var channels = Format.Channels;
        var got = wanted == 0 ? 0 : _decoder.Read(Samples.AsSpan(at * channels, wanted * channels));
        _next += got;
        Volatile.Write(ref _written, _written + got);
        if (got < wanted || (!loops && _next == end))
        {
            Finish();
            return false;
        }

        if (_next == end)
        {
            Seek(sound.Loop.Start);
        }

        return true;
    }

    /// <summary>No more frames come: the voice ends with those there are.</summary>
    public void Finish()
    {
        var decoder = _decoder;
        _decoder = null;
        Volatile.Write(ref _hasAll, true);
        decoder?.Dispose();
    }

    private void Begin()
    {
        _decoder = SoundFile.Open(sound.Storage.Open());
        if (_decoder.Format != Format)
        {
            throw new InvalidDataException("The sound file is another now than it was.");
        }

        Seek(SoundFrame);
    }

    private void Seek(int frame)
    {
        if (frame != 0 || _next != 0)
        {
            _decoder.Seek(frame);
        }

        _next = frame;
    }
}
