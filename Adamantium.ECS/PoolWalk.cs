using Adamantium.ECS.Components;

namespace Adamantium.ECS;

// Walks the pools of a selection: each component whose entity is enabled in its tree, a pool after another. What a pool
// holds when the walk comes to it is walked; what is added after is not, and a gap left by a component taken out is
// stepped over.
internal struct PoolWalk
{
    private readonly ComponentPool[] _pools;
    private int _pool;
    private int _item;
    private int _end;

    public PoolWalk(ComponentPool[] pools)
    {
        _pools = pools;
        _pool = 0;
        _item = -1;
        _end = pools.Length > 0 ? pools[0].Count : 0;
        Component = null;
        Owner = null;
    }

    public IComponent Component { get; private set; }

    public Entity Owner { get; private set; }

    public bool MoveNext()
    {
        while (_pool < _pools.Length)
        {
            var pool = _pools[_pool];
            while (++_item < _end)
            {
                var owner = pool.Owners[_item];
                if (owner != null && owner.IsEnabledInTree)
                {
                    Component = pool.Items[_item];
                    Owner = owner;
                    return true;
                }
            }

            _pool++;
            _item = -1;
            _end = _pool < _pools.Length ? _pools[_pool].Count : 0;
        }

        Component = null;
        Owner = null;
        return false;
    }
}
