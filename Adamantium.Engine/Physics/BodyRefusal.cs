namespace Adamantium.Engine.Physics;

/// <summary>A body the physics world would not take, told back to the game's side.</summary>
internal readonly struct BodyRefusal
{
    public BodyRefusal(int key, int generation)
    {
        Key = key;
        Generation = generation;
    }

    public int Key { get; }

    public int Generation { get; }
}
