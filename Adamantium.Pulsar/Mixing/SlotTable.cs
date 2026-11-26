namespace Adamantium.Pulsar.Mixing;

/// <summary>Which slots are in use, told apart by a generation from those that were in their place.</summary>
internal sealed class SlotTable
{
    private readonly int[] _generations;
    private readonly bool[] _used;
    private readonly Stack<int> _free;

    public SlotTable(int slots)
    {
        _generations = new int[slots];
        _used = new bool[slots];
        _free = new Stack<int>(slots);
        for (var slot = slots - 1; slot >= 0; slot--)
        {
            _free.Push(slot);
        }
    }

    public int Count => _generations.Length - _free.Count;

    public int GenerationOf(int slot) => _generations[slot];

    public bool Holds(int slot, int generation) =>
        (uint)slot < (uint)_generations.Length && _used[slot] && _generations[slot] == generation;

    /// <summary>Takes the lowest one never taken, or the last one freed.</summary>
    public bool TryTake(out int slot)
    {
        if (!_free.TryPop(out slot))
        {
            return false;
        }

        _used[slot] = true;
        _generations[slot]++;
        return true;
    }

    /// <summary>It is in use no more, and can not be taken until <see cref="Free"/>.</summary>
    public void Retire(int slot) => _used[slot] = false;

    public void Free(int slot)
    {
        _used[slot] = false;
        _free.Push(slot);
    }
}
