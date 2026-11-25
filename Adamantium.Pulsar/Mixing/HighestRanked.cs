namespace Adamantium.Pulsar.Mixing;

/// <summary>Of the slots added, the highest ranked, as many as there is room for: a heap with the lowest of them on top.
/// </summary>
internal sealed class HighestRanked(int room)
{
    private readonly long[] _ranks = new long[room];
    private readonly int[] _slots = new int[room];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    /// <summary>The slot there is no room for, the one added or another; -1 where there was room.</summary>
    public int Add(long rank, int slot)
    {
        if (Count < _ranks.Length)
        {
            Raise(Count++, rank, slot);
            return -1;
        }

        if (rank <= _ranks[0])
        {
            return slot;
        }

        var dropped = _slots[0];
        Lower(rank, slot);
        return dropped;
    }

    private void Raise(int at, long rank, int slot)
    {
        while (at > 0 && _ranks[(at - 1) / 2] > rank)
        {
            Put(at, _ranks[(at - 1) / 2], _slots[(at - 1) / 2]);
            at = (at - 1) / 2;
        }

        Put(at, rank, slot);
    }

    private void Lower(long rank, int slot)
    {
        var at = 0;
        while (at * 2 + 1 < Count)
        {
            var child = at * 2 + 1;
            if (child + 1 < Count && _ranks[child + 1] < _ranks[child])
            {
                child++;
            }

            if (_ranks[child] >= rank)
            {
                break;
            }

            Put(at, _ranks[child], _slots[child]);
            at = child;
        }

        Put(at, rank, slot);
    }

    private void Put(int at, long rank, int slot)
    {
        _ranks[at] = rank;
        _slots[at] = slot;
    }
}
