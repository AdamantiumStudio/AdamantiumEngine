using System.Threading;

namespace Adamantium.ECS;

// Hands each component type asked for by an entity a number of its own, the place of its answer in the entity's cache.
internal static class ComponentSlots
{
    private static int _count;

    public static int Take() => Interlocked.Increment(ref _count) - 1;
}
