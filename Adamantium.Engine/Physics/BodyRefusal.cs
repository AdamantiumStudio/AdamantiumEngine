namespace Adamantium.Engine.Physics;

/// <summary>A body the physics world would not take, or a change to one it would not take, told back to the game's
/// side.</summary>
internal readonly struct BodyRefusal
{
    public BodyRefusal(int key, int generation, int number)
    {
        Key = key;
        Generation = generation;
        Number = number;
    }

    public int Key { get; }

    /// <summary>The generation the body was added with.</summary>
    public int Generation { get; }

    /// <summary>The number of the refused request among those sent for the body.</summary>
    public int Number { get; }
}
