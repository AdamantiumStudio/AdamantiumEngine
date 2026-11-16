namespace Adamantium.ECS;

// The number of the type T among the types entities are asked for: taken once, the first time T is asked for.
internal static class ComponentSlot<T>
{
    public static readonly int Index = ComponentSlots.Take();
}
