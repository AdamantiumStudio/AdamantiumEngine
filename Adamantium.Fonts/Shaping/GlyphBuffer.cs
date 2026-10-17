using System;

namespace Adamantium.Fonts.Shaping;

internal sealed class GlyphBuffer
{
    public const int MostKept = 16384;

    private int _serial;

    public GlyphBuffer(int capacity)
    {
        Info = new GlyphInfo[Math.Max(capacity, 4)];
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
            Placements = new GlyphPlacement[Math.Max(Length, Info.Length)];
        }
        else
        {
            Array.Clear(Placements, 0, Length);
        }
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

        var resized = Info;
        Array.Resize(ref resized, Math.Max(capacity, Info.Length * 2));
        Info = resized;
    }
}
