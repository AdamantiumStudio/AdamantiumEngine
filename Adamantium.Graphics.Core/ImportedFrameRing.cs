namespace Adamantium.Graphics.Core;

/// <summary>A ring of frames in memory of the process as a main device's surfaces take it: mapped once, and its slots
/// imported once where the device takes the process's memory, for every surface showing frames of it, whatever their
/// size. A slot a surface still copies from is not given back for another surface's newer frame. Held through
/// <see cref="HostFrameRings.Hold"/>, let go of with <see cref="Release"/>; the last to let go of it closes it.</summary>
public sealed class ImportedFrameRing
{
    private readonly HostFrameRings _rings;
    private readonly int[] _copying;

    internal ImportedFrameRing(HostFrameRings rings, string path, HostFrameReader reader, HostSlotBuffer[] slots)
    {
        _rings = rings;
        Path = path;
        Reader = reader;
        Slots = slots;
        _copying = new int[reader.SlotCount];
    }

    /// <summary>The file the ring is shared through.</summary>
    public string Path { get; }

    public HostFrameReader Reader { get; }

    /// <summary>The slots imported as buffers; null where the device takes none of the process's memory.</summary>
    public HostSlotBuffer[] Slots { get; }

    internal int Holders { get; set; }

    /// <summary>Says a copy from <paramref name="slot"/> was submitted: it is not given back until
    /// <see cref="EndCopy"/>.</summary>
    public void BeginCopy(int slot) => _copying[slot]++;

    /// <summary>Says the copy from <paramref name="slot"/> is done, and gives back <paramref name="frame"/> and the older
    /// frames no surface still copies from.</summary>
    public void EndCopy(int slot, ulong frame)
    {
        _copying[slot]--;
        GiveBack(frame);
    }

    /// <summary>Gives back <paramref name="frame"/> and the older frames no surface still copies from.</summary>
    public void GiveBack(ulong frame) => Reader.GiveBack(frame, _copying);

    /// <summary>Lets go of the ring held; the last to let go of it closes it.</summary>
    public void Release() => _rings.Release(this);

    internal void Close()
    {
        if (Slots != null)
        {
            foreach (var slot in Slots)
            {
                slot?.Dispose();
            }
        }

        Reader.Dispose();
    }
}
