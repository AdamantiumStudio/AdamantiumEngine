namespace Adamantium.Pulsar.Mixing;

/// <summary>The last stereo frames mixed somewhere - the master, or a bus before its volume - kept round for what shows
/// the sound: written on the output's thread, read on any without a lock, so a block mixed meanwhile may be in part of
/// what is read.</summary>
internal sealed class PlayedFrames
{
    /// <summary>85 ms at 48 kHz.</summary>
    public const int Capacity = 4096;

    private readonly float[] _frames = new float[2 * Capacity];
    // How many were ever.
    private long _end;

    /// <summary>On the output's thread: <paramref name="samples"/>, stereo; none for as many frames of silence.</summary>
    public void Keep(ReadOnlySpan<float> samples, int frames)
    {
        var count = Math.Min(frames, Capacity);
        var at = (int)((_end + frames - count) % Capacity);
        var first = Math.Min(count, Capacity - at);
        if (samples.IsEmpty)
        {
            _frames.AsSpan(2 * at, 2 * first).Clear();
            _frames.AsSpan(0, 2 * (count - first)).Clear();
        }
        else
        {
            var kept = samples[(2 * (frames - count))..];
            kept[..(2 * first)].CopyTo(_frames.AsSpan(2 * at));
            kept[(2 * first)..].CopyTo(_frames);
        }

        Volatile.Write(ref _end, _end + frames);
    }

    /// <summary>The latest, as many as <paramref name="frames"/> holds, up to <see cref="Capacity"/>; how many there
    /// were.</summary>
    public int CopyTo(Span<float> frames)
    {
        var end = Volatile.Read(ref _end);
        var count = (int)Math.Min(Math.Min(frames.Length / 2, Capacity), end);
        var from = (int)((end - count) % Capacity);
        var first = Math.Min(count, Capacity - from);
        _frames.AsSpan(2 * from, 2 * first).CopyTo(frames);
        _frames.AsSpan(0, 2 * (count - first)).CopyTo(frames[(2 * first)..]);
        return count;
    }
}
