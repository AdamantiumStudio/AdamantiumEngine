namespace Adamantium.Pulsar.Mixing;

/// <summary>Slots the mixer is done with, on their way back to the game. Each is out once at most, so there is room.</summary>
internal sealed class SlotReturns(int slots)
{
    private readonly int[] _slots = new int[slots];
    private int _added;
    private int _taken;

    public void Add(int slot)
    {
        _slots[_added % _slots.Length] = slot;
        Volatile.Write(ref _added, (_added + 1) % (_slots.Length * 2));
    }

    public bool TryTake(out int slot)
    {
        if (_taken == Volatile.Read(ref _added))
        {
            slot = 0;
            return false;
        }

        slot = _slots[_taken % _slots.Length];
        _taken = (_taken + 1) % (_slots.Length * 2);
        return true;
    }
}
