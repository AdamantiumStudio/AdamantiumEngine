using System;

namespace Adamantium.Pdf.Filters;

/// <summary>A growing byte buffer that refuses to grow past a limit. What it holds is handed out as it is, not copied.
/// </summary>
internal sealed class PdfOutputBuffer
{
    private readonly int _limit;
    private byte[] _bytes;
    private int _count;

    public PdfOutputBuffer(int capacity, int limit)
    {
        _limit = limit;
        _bytes = new byte[Math.Clamp(capacity, 16, Math.Max(16, limit))];
    }

    public int Count => _count;

    public int Limit => _limit;

    public void Add(byte value)
    {
        Reserve(1);
        _bytes[_count++] = value;
    }

    public void Add(ReadOnlySpan<byte> values)
    {
        Reserve(values.Length);
        values.CopyTo(_bytes.AsSpan(_count));
        _count += values.Length;
    }

    public void Repeat(byte value, int count)
    {
        Reserve(count);
        _bytes.AsSpan(_count, count).Fill(value);
        _count += count;
    }

    /// <summary>Room for up to <paramref name="count"/> more bytes, to write into directly; empty once the limit is
    /// reached.</summary>
    public Span<byte> Free(int count)
    {
        count = (int)Math.Min(count, (long)_limit - _count);
        if (count <= 0)
        {
            return Span<byte>.Empty;
        }

        Reserve(count);
        return _bytes.AsSpan(_count, count);
    }

    public void Advance(int count) => _count += count;

    public Memory<byte> ToMemory() => _bytes.AsMemory(0, _count);

    private void Reserve(int count)
    {
        var needed = (long)_count + count;
        if (needed <= _bytes.Length)
        {
            return;
        }

        if (needed > _limit)
        {
            throw new PdfDecodeLimitException(_limit);
        }

        var size = (int)Math.Min(_limit, Math.Max(needed, (long)_bytes.Length * 2));
        Array.Resize(ref _bytes, size);
    }
}
