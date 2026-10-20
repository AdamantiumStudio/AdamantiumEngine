using System;
using System.Buffers;

namespace Adamantium.Fonts.Shaping;

internal sealed class GlyphBuffer : IDisposable
{
    public const int MostPooled = 16384;

    private int _serial;

    public GlyphBuffer(int capacity)
    {
        Info = Rent<GlyphInfo>(Math.Max(capacity, 4));
    }

    public static T[] Rent<T>(int length) => length <= MostPooled ? ArrayPool<T>.Shared.Rent(length) : new T[length];

    public static void Return<T>(T[] array)
    {
        if (array.Length <= MostPooled)
        {
            ArrayPool<T>.Shared.Return(array);
        }
    }

    public GlyphInfo[] Info { get; private set; }

    public GlyphPlacement[] Placements { get; private set; }

    public int Length { get; private set; }

    public bool HasDefaultIgnorables { get; set; }

    public bool HasSpaceFallback { get; set; }

    public bool HasNonAscii { get; set; }

    public void Add(in GlyphInfo info)
    {
        EnsureCapacity(Length + 1);
        Info[Length++] = info;
    }

    public void Clear()
    {
        Length = 0;
    }

    public void Reset(int capacity)
    {
        Length = 0;
        _serial = 0;
        HasDefaultIgnorables = false;
        HasSpaceFallback = false;
        HasNonAscii = false;
        EnsureCapacity(capacity);
    }

    public void Insert(int index, in GlyphInfo info)
    {
        EnsureCapacity(Length + 1);
        Array.Copy(Info, index, Info, index + 1, Length - index);
        Info[index] = info;
        Length++;
    }

    public void RemoveAt(int index)
    {
        Array.Copy(Info, index + 1, Info, index, Length - index - 1);
        Length--;
    }

    public void Move(int from, int to)
    {
        var moved = Info[from];
        if (from > to)
        {
            Array.Copy(Info, to, Info, to + 1, from - to);
        }
        else
        {
            Array.Copy(Info, from + 1, Info, from, to - from);
        }

        Info[to] = moved;
    }

    public void MergeClusters(int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        var cluster = Info[start].Cluster;
        for (var i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, Info[i].Cluster);
        }

        if (cluster != Info[end - 1].Cluster)
        {
            while (end < Length && Info[end - 1].Cluster == Info[end].Cluster)
            {
                end++;
            }
        }

        if (cluster != Info[start].Cluster)
        {
            while (start > 0 && Info[start - 1].Cluster == Info[start].Cluster)
            {
                start--;
            }
        }

        for (var i = start; i < end; i++)
        {
            Info[i].Cluster = cluster;
        }
    }

    public int AllocateLigId()
    {
        var id = ++_serial & 0x07;
        if (id == 0)
        {
            id = ++_serial & 0x07;
        }

        return id;
    }

    public void ClearPlacements()
    {
        if (Placements == null || Placements.Length < Length)
        {
            var old = Placements;
            Placements = Rent<GlyphPlacement>(Math.Max(Length, 4));
            if (old != null)
            {
                Return(old);
            }
        }

        Array.Clear(Placements, 0, Length);
    }

    public void Dispose()
    {
        if (Info != null)
        {
            Return(Info);
            Info = null;
        }

        if (Placements != null)
        {
            Return(Placements);
            Placements = null;
        }

        Length = 0;
    }

    public void RemoveWhere(Func<GlyphInfo, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < Length; i++)
        {
            if (predicate(Info[i]))
            {
                continue;
            }

            Info[count] = Info[i];
            if (Placements != null)
            {
                Placements[count] = Placements[i];
            }

            count++;
        }

        Length = count;
    }

    private void EnsureCapacity(int capacity)
    {
        if (capacity <= Info.Length)
        {
            return;
        }

        var resized = Rent<GlyphInfo>(Math.Max(capacity, Info.Length * 2));
        Array.Copy(Info, resized, Length);
        Return(Info);
        Info = resized;
    }
}
